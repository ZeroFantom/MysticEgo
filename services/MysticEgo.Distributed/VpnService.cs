using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public interface IVpnAccessPolicy
{
    string? Actor(ClaimsPrincipal user);
    bool CanManage(ClaimsPrincipal user,bool developerOnly);
}
public sealed class VpnAccessPolicy : IVpnAccessPolicy
{
    public string? Actor(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && user.FindFirst("token_use")?.Value != "device"
        ? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value ?? user.Identity.Name : null;
    public bool CanManage(ClaimsPrincipal user,bool developerOnly) => Actor(user) is not null &&
        (EcosystemHierarchy.IsDeveloper(user) || (!developerOnly && (user.IsInRole("global.manager") || user.HasClaim("permission","vpn.manage"))));
}
public interface IVpnService
{
    Task<object> MyRequests(string user,CancellationToken ct);
    Task<object> Apply(string user,VpnApply input,CancellationToken ct);
    Task<object> Subscription(string user,Guid id,CancellationToken ct);
    Task<object> Requests(CancellationToken ct);
    Task<object> Decide(Guid id,VpnDecision decision,string actor,CancellationToken ct);
    object DeploymentPlan(VpnDeployDraft draft);
    Task<object> Panels(CancellationToken ct);
    Task<object> AddPanel(VpnPanelCreate request,string actor,CancellationToken ct);
}

public interface IVpnPanelGateway
{
    Task<(bool Success,string? Error)> Verify(VpnPanel panel,string email,CancellationToken ct);
    Task<(bool Success,string? Error)> Delete(VpnPanel panel,string email,CancellationToken ct);
}
public sealed class VpnPanelGateway(IConfiguration config) : IVpnPanelGateway
{
    public async Task<(bool Success,string? Error)> Verify(VpnPanel panel,string email,CancellationToken ct)
    {
        var response=await Vpn3xUiClient.VerifyClientAsync(panel,email,config,ct);
        return (response.Success,response.Error);
    }
    public async Task<(bool Success,string? Error)> Delete(VpnPanel panel,string email,CancellationToken ct)
    {
        var response=await Vpn3xUiClient.DeleteClientAsync(panel,email,config,ct);
        return (response.Success,response.Error);
    }
}

public sealed class VpnService(NodeState state,IConfiguration config,IVpnPanelGateway gateway) : IVpnService
{
    private readonly SemaphoreSlim _lock = new(1,1);
    private static bool SafeHttps(string? url) => Uri.TryCreate(url,UriKind.Absolute,out var u) && u.Scheme==Uri.UriSchemeHttps &&
        !string.IsNullOrWhiteSpace(u.Host) && u.UserInfo.Length==0 && u.Fragment.Length==0;
    private byte[] Key()
    {
        var encoded=config["Vpn:LinkEncryptionKey"];
        byte[] key;
        try {key=Convert.FromBase64String(encoded??"");}
        catch(FormatException){throw new NodeApiException(503,"VPN link encryption key not configured");}
        if(key.Length!=32)throw new NodeApiException(503,"VPN link encryption key not configured");
        return key;
    }
    private static string Encrypt(string value,byte[] key)
    {
        var nonce=RandomNumberGenerator.GetBytes(12);var data=Encoding.UTF8.GetBytes(value);
        var cipher=new byte[data.Length];var tag=new byte[16];
        using var aes=new AesGcm(key,16);aes.Encrypt(nonce,data,cipher,tag);
        return Convert.ToBase64String(nonce.Concat(tag).Concat(cipher).ToArray());
    }
    private static string Decrypt(string value,byte[] key)
    {
        try
        {
            var blob=Convert.FromBase64String(value);
            if(blob.Length<28)throw new CryptographicException("Invalid subscription cipher");
            var clear=new byte[blob.Length-28];
            using var aes=new AesGcm(key,16);
            aes.Decrypt(blob.AsSpan(0,12),blob.AsSpan(28),blob.AsSpan(12,16),clear);
            return Encoding.UTF8.GetString(clear);
        }
        catch(Exception ex) when(ex is FormatException or CryptographicException)
        { throw new NodeApiException(503,"Subscription data could not be read"); }
    }
    public async Task<object> MyRequests(string user,CancellationToken ct) =>
        (await state.Data.ListAsync<VpnRequest>(10000,ct)).Where(x=>x.UserId==user).OrderByDescending(x=>x.CreatedAt)
        .Select(x=>new{x.Id,x.Region,x.Reason,x.Status,x.CreatedAt,x.UpdatedAt,hasSubscription=x.Status=="approved"&&x.EncryptedSubscription.Length>0}).ToArray();
    public async Task<object> Apply(string user,VpnApply input,CancellationToken ct)
    {
        if(input.Reason is null||input.Reason.Length>500||input.Region is null||input.Region.Length>48)
            throw new NodeApiException(400,"Invalid form");
        await _lock.WaitAsync(ct);
        try
        {
            var all=await state.Data.ListAsync<VpnRequest>(10000,ct);
            if(all.Any(x=>x.UserId==user && x.Status is "pending" or "approved" or "revocation_pending"))
                throw new NodeApiException(409,"Active request already exists");
            var item=new VpnRequest{Id=Guid.NewGuid(),UserId=user,Reason=input.Reason.Trim(),Region=input.Region.Trim()};
            await state.Data.PutAsync(item,ct);state.Log(user,"vpn.request.created",item.Id.ToString(),true);
            return new{item.Id,item.Status,item.CreatedAt};
        }
        finally{_lock.Release();}
    }
    public async Task<object> Subscription(string user,Guid id,CancellationToken ct)
    {
        var item=(await state.Data.ListAsync<VpnRequest>(10000,ct)).FirstOrDefault(x=>x.Id==id&&x.UserId==user);
        if(item is null)throw new NodeApiException(404,"Request not found");
        if(item.Status!="approved"||item.EncryptedSubscription.Length==0)throw new NodeApiException(409,"Not approved");
        var link=Decrypt(item.EncryptedSubscription,Key());
        return new{url=link,qrContent=link};
    }
    public async Task<object> Requests(CancellationToken ct) =>
        (await state.Data.ListAsync<VpnRequest>(10000,ct)).OrderByDescending(x=>x.CreatedAt)
        .Select(x=>new{x.Id,x.UserId,x.Region,x.Reason,x.Status,x.PanelId,x.ClientEmail,x.CreatedAt,x.UpdatedAt}).ToArray();
    public async Task<object> Decide(Guid id,VpnDecision input,string actor,CancellationToken ct)
    {
        if(input.Status is not ("approved" or "rejected" or "revoked"))throw new NodeApiException(400,"Invalid status");
        byte[]? key=null;
        if(input.Status=="approved")
        {
            if(!SafeHttps(input.SubscriptionUrl)||!Guid.TryParse(input.PanelId,out _)||!Vpn3xUiClient.IsValidClientName(input.ClientEmail))
                throw new NodeApiException(400,"Approval requires HTTPS subscription, registered panel and 3x-ui client");
            key=Key();
        }
        await _lock.WaitAsync(ct);
        try
        {
            var item=(await state.Data.ListAsync<VpnRequest>(10000,ct)).FirstOrDefault(x=>x.Id==id);
            if(item is null)throw new NodeApiException(404,"Request not found");
            if(item.Status is "rejected" or "revoked")throw new NodeApiException(409,"Finalized request");
            if(input.Status=="approved")
            {
                if(item.Status!="pending")throw new NodeApiException(409,"Only pending requests may be approved");
                var panel=(await state.Data.ListAsync<VpnPanel>(1000,ct)).FirstOrDefault(x=>x.Id.ToString().Equals(input.PanelId,StringComparison.OrdinalIgnoreCase));
                if(panel is null)throw new NodeApiException(400,"Unknown registered panel");
                var verified=await gateway.Verify(panel,input.ClientEmail!,ct);
                if(!verified.Success)throw new NodeApiException(502,"3x-ui client not verified: "+verified.Error);
                item.Status="approved";item.EncryptedSubscription=Encrypt(input.SubscriptionUrl!,key!);
                item.PanelId=panel.Id.ToString();item.ClientEmail=input.ClientEmail!;
                item.UpdatedAt=DateTimeOffset.UtcNow;
                await state.Data.PutAsync(item,ct);state.Log(actor,"vpn.request.approved",id.ToString(),true);
                return new{item.Id,item.Status,item.UpdatedAt};
            }
            if(input.Status=="rejected")
            {
                if(item.Status!="pending")throw new NodeApiException(409,"Cannot reject approved request; revoke it");
                item.Status="rejected";item.UpdatedAt=DateTimeOffset.UtcNow;
                await state.Data.PutAsync(item,ct);state.Log(actor,"vpn.request.rejected",id.ToString(),true);
                return new{item.Id,item.Status,item.UpdatedAt};
            }
            if(item.Status is not ("approved" or "revocation_pending"))
                throw new NodeApiException(409,"Only approved or pending revocation requests may be revoked");
            item.Status="revocation_pending";item.EncryptedSubscription="";item.UpdatedAt=DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item,ct);
            if(!Guid.TryParse(item.PanelId,out var panelId)||!Vpn3xUiClient.IsValidClientName(item.ClientEmail))
                throw new NodeApiException(409,"Missing remote binding: manual 3x-ui revocation required");
            var remote=(await state.Data.ListAsync<VpnPanel>(1000,ct)).FirstOrDefault(x=>x.Id==panelId);
            if(remote is null)throw new NodeApiException(409,"Remote panel not registered");
            var result=await gateway.Delete(remote,item.ClientEmail,ct);
            if(!result.Success)
            {
                state.Log(actor,"vpn.request.revocation_pending",id.ToString(),false);
                throw new NodeApiException(502,"Remote revocation not confirmed: "+result.Error);
            }
            item.Status="revoked";item.UpdatedAt=DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item,ct);state.Log(actor,"vpn.request.revoked",id.ToString(),true);
            return new{item.Id,item.Status,item.UpdatedAt};
        }
        finally{_lock.Release();}
    }
    public object DeploymentPlan(VpnDeployDraft draft)
    {
        if(draft.Nodes is null||draft.Nodes.Length is <1 or >64||draft.Nodes.Any(n=>
            string.IsNullOrWhiteSpace(n.Host)||
            !System.Text.RegularExpressions.Regex.IsMatch(n.Host,@"^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$")||
            !(SafeHttps(n.GlobalUrl)||SafeHttps(n.OrchestraUrl))))throw new NodeApiException(400,"Invalid VPN deploy targets");
        return new{nodes=draft.Nodes.Select(n=>new{host=n.Host,roles=new[]{"vpn"},globalUrl=n.GlobalUrl,orchestraUrl=n.OrchestraUrl,region=n.Region}).ToArray()};
    }
    public async Task<object> Panels(CancellationToken ct) => await state.Data.ListAsync<VpnPanel>(1000,ct);
    public async Task<object> AddPanel(VpnPanelCreate input,string actor,CancellationToken ct)
    {
        if(input.Label is null||input.Label.Length is <1 or >100||input.Region is null||input.Region.Length>48||!SafeHttps(input.BaseUrl))
            throw new NodeApiException(400,"A valid HTTPS 3x-ui panel URL is required");
        var panel=new VpnPanel{Id=Guid.NewGuid(),Label=input.Label.Trim(),BaseUrl=input.BaseUrl.TrimEnd('/'),Region=input.Region.Trim()};
        await state.Data.PutAsync(panel,ct);state.Log(actor,"vpn.panel.register",panel.Id.ToString(),true);
        return panel;
    }
}
public static class VpnDependencyInjection
{
    public static IServiceCollection AddVpnServices(this IServiceCollection services)
    {
        services.AddSingleton<IVpnAccessPolicy,VpnAccessPolicy>();
        services.AddSingleton<IVpnPanelGateway,VpnPanelGateway>();
        services.AddSingleton<IVpnService,VpnService>();
        return services;
    }
}
