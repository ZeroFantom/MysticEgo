using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public interface ISetupAccessPolicy
{
    bool Manager(ClaimsPrincipal user);
    string? Human(ClaimsPrincipal user);
}
public sealed class SetupAccessPolicy(NodeOptions node) : ISetupAccessPolicy
{
    public string? Human(ClaimsPrincipal user) => user.Identity?.IsAuthenticated==true && user.FindFirst("token_use")?.Value!="device"
        ? user.Identity.Name??user.FindFirst(ClaimTypes.NameIdentifier)?.Value:null;
    public bool Manager(ClaimsPrincipal user) => Human(user) is not null &&
        (EcosystemHierarchy.IsDeveloper(user) || user.HasClaim("permission",node.Kind==NodeKind.Orchestra?"orchestra.setup.manage":"organization.setup.manage") ||
         node.Kind==NodeKind.Global&&user.IsInRole("global.manager") || node.Kind==NodeKind.Local&&user.IsInRole("local.admin") ||
         node.Kind==NodeKind.AuditOrchestra && string.Equals(Human(user),Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER"),StringComparison.Ordinal));
}
public interface ISetupService
{
    Task<object> State(CancellationToken ct);
    Task<object> ConnectParent(SetupJoinParentRequest request,string actor,CancellationToken ct);
    Task<object?> Organization(CancellationToken ct);
    Task<SetupOrganization> CreateOrganization(SetupOrganizationRequest request,string actor,CancellationToken ct);
    Task<object> InviteTeam(CreateTeamInviteRequest request,string actor,CancellationToken ct);
    Task<object> JoinTeam(JoinTeamRequest request,string actor,CancellationToken ct);
    Task<object> InviteNode(SetupInviteRequest request,string actor,CancellationToken ct);
    Task<object> Enroll(SetupEnrollRequest request,IPAddress? remoteAddress,bool secure,CancellationToken ct);
}
public sealed class SetupService(NodeOptions node,NodeState state,IHttpClientFactory clients) : ISetupService
{
    private readonly SemaphoreSlim _enrollment=new(1,1);
    private readonly ConcurrentDictionary<string,(int Count,DateTimeOffset Until)> _rejections=new();
    private static bool Text(string? value,int min=2,int max=100) => value is not null && value.Trim().Length>=min&&value.Trim().Length<=max;
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private string? ParentUrl() => node.Kind switch
    {
        NodeKind.Global=>node.OrchestraUrl,NodeKind.Local=>node.GlobalUrl,NodeKind.Agent=>node.LocalUrl,
        NodeKind.AuditOrchestra=>node.OrchestraUrl,_=>null
    };
    public async Task<object> State(CancellationToken ct)
    {
        var organizations=await state.Data.ListAsync<SetupOrganization>(10,ct);
        return new{node=node.NodeId,kind=node.Kind.ToString(),configured=organizations.Count>0,parentUrl=ParentUrl(),rank="F",level=1};
    }
    public async Task<object> ConnectParent(SetupJoinParentRequest request,string actor,CancellationToken ct)
    {
        var parent=ParentUrl();
        if(!Uri.TryCreate(parent,UriKind.Absolute,out var uri) || !(uri.Scheme==Uri.UriSchemeHttps ||
            node.DeploymentMode=="single-machine" && uri.Scheme==Uri.UriSchemeHttp && uri.IsLoopback))
            throw new NodeApiException(503,"Родительский HTTPS API не настроен.");
        if(request.Code is null || !Regex.IsMatch(request.Code,@"^[0-9A-Fa-f]{64}$"))
            throw new NodeApiException(400,"Некорректный одноразовый код.");
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var response=await clients.CreateClient().PostAsJsonAsync(parent!.TrimEnd('/')+"/api/v1/setup/enroll",
                new SetupEnrollRequest(request.Code,node.Kind==NodeKind.AuditOrchestra?"audit":node.Kind.ToString().ToLowerInvariant(),node.NodeId,node.DisplayName),timeout.Token);
            if(!response.IsSuccessStatusCode)throw new NodeApiException((int)response.StatusCode,"Родительский узел отклонил приглашение.");
            var receipt=await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken:timeout.Token);
            state.Log(actor,"setup.join",node.NodeId,true);
            return receipt;
        }
        catch(HttpRequestException){throw new NodeApiException(502,"Нет связи с родительским сервером.");}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new NodeApiException(504,"Таймаут регистрации.");}
    }
    public async Task<object?> Organization(CancellationToken ct) =>
        (await state.Data.ListAsync<SetupOrganization>(100,ct)).OrderByDescending(x=>x.CreatedAt).FirstOrDefault();
    public async Task<SetupOrganization> CreateOrganization(SetupOrganizationRequest input,string actor,CancellationToken ct)
    {
        if(node.Kind is not (NodeKind.Orchestra or NodeKind.Global))throw new NodeApiException(400,"Настройка поддерживается только для Orchestra/Global.");
        if(!Text(input.Name)||!Regex.IsMatch(input.FounderLogin??"",@"^[a-zA-Z_][a-zA-Z0-9_.-]{0,31}$") ||
           input.Departments is null||input.Departments.Length is <1 or >100 ||
           input.Departments.Any(x=>!Text(x.Name)||x.Members is <1 or >10000)||
           input.Departments.Select(x=>x.Name.Trim().ToUpperInvariant()).Distinct().Count()!=input.Departments.Length||
           input.Sites is null||input.Sites.Length>1000||(node.Kind==NodeKind.Global&&input.Sites.Length==0)||
           input.Sites.Any(x=>!Text(x.Name)||!Text(x.Address,3,300)||x.Computers is <0 or >10000)||
           new[]{input.StorageDatabase,input.StorageCloud,input.StorageLocal}.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>512)||
           new[]{input.StorageDatabase,input.StorageCloud,input.StorageLocal}.Distinct().Count()!=3)
            throw new NodeApiException(400,"Проверьте организацию, отделы, филиалы и пути хранения.");
        if((await state.Data.ListAsync<SetupOrganization>(10,ct)).Count>0)
            throw new NodeApiException(409,"Организация уже настроена. Изменения выполняются отдельной миграцией.");
        var entity=new SetupOrganization{Id=Guid.NewGuid(),Name=input.Name.Trim(),FounderLogin=input.FounderLogin.Trim(),
            Kind=node.Kind.ToString(),DepartmentsJson=JsonSerializer.Serialize(input.Departments),SitesJson=JsonSerializer.Serialize(input.Sites),
            StorageDatabase=input.StorageDatabase,StorageCloud=input.StorageCloud,StorageLocal=input.StorageLocal};
        await state.Data.PutAsync(entity,ct);state.Log(actor,"setup.organization",entity.Id.ToString(),true);
        return entity;
    }
    public async Task<object> InviteTeam(CreateTeamInviteRequest input,string actor,CancellationToken ct)
    {
        if(node.Kind is not (NodeKind.Orchestra or NodeKind.Global))throw new NodeApiException(403,"Team invitation not allowed");
        var org=(await state.Data.ListAsync<SetupOrganization>(5,ct)).FirstOrDefault();
        if(org is null)throw new NodeApiException(400,"Сначала зарегистрируйте организацию.");
        var departments=JsonSerializer.Deserialize<SetupDepartment[]>(org.DepartmentsJson)??Array.Empty<SetupDepartment>();
        if(!departments.Any(x=>x.Name==input.Department)||!Text(input.Label)||input.ValidHours is <1 or >168)
            throw new NodeApiException(400,"Неверный отдел, метка или срок.");
        var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var ticket=new SetupTeamInvitation{Id=Guid.NewGuid(),CodeHash=Hash(code),Department=input.Department,Label=input.Label,
            ExpiresAt=DateTimeOffset.UtcNow.AddHours(input.ValidHours)};
        await state.Data.PutAsync(ticket,ct);state.Log(actor,"setup.team.invite",input.Department,true);
        return new{code,department=input.Department,expiresAt=ticket.ExpiresAt,notice="Код одноразовый; права назначаются отдельно в Auth."};
    }
    public async Task<object> JoinTeam(JoinTeamRequest input,string actor,CancellationToken ct)
    {
        if(input.InviteCode is null||!Regex.IsMatch(input.InviteCode,@"^[0-9A-Fa-f]{64}$"))throw new NodeApiException(400,"Invalid invitation");
        await _enrollment.WaitAsync(ct);
        try
        {
            var hashed=Hash(input.InviteCode.ToUpperInvariant());
            var ticket=(await state.Data.ListAsync<SetupTeamInvitation>(5000,ct)).FirstOrDefault(x=>x.CodeHash==hashed);
            if(ticket is null||ticket.RegisteredLogin is not null||ticket.ExpiresAt<=DateTimeOffset.UtcNow)
                throw new NodeApiException(401,"Invitation expired or used");
            ticket.RegisteredLogin=actor;await state.Data.PutAsync(ticket,ct);
            state.Log(actor,"setup.team.join",ticket.Department,true);
            return new{registered=true,department=ticket.Department,roleAssigned=false,
                note="Заявка сохранена; привилегии назначает Auth-администратор после проверки."};
        }
        finally{_enrollment.Release();}
    }
    public async Task<object> InviteNode(SetupInviteRequest input,string actor,CancellationToken ct)
    {
        var allowed=node.Kind switch
        {
            NodeKind.Orchestra=>input.ChildKind is "global" or "audit",NodeKind.Global=>input.ChildKind=="local",
            NodeKind.Local=>input.ChildKind=="agent",_=>false
        };
        if(!allowed||input.ValidMinutes is <1 or >1440)throw new NodeApiException(400,"Недопустимый дочерний узел или срок приглашения.");
        var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var ticket=new SetupInvitation{Id=Guid.NewGuid(),Hash=Hash(code),ChildKind=input.ChildKind,
            ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(input.ValidMinutes),IssuedBy=actor};
        await state.Data.PutAsync(ticket,ct);state.Log(actor,"setup.invitation",input.ChildKind,true,ticket.Id.ToString());
        return new{code,validUntil=ticket.ExpiresAt,childKind=ticket.ChildKind,
            notice="Код показывается один раз. Устройство получает идентичность отдельно через Auth."};
    }
    public async Task<object> Enroll(SetupEnrollRequest input,IPAddress? remote,bool secure,CancellationToken ct)
    {
        var address=remote?.ToString()??"unknown";var now=DateTimeOffset.UtcNow;
        if(_rejections.TryGetValue(address,out var attempts)&&attempts.Until>now&&attempts.Count>=10)
            throw new NodeApiException(429,"Too many rejected invitations");
        if(!secure&&!IPAddress.IsLoopback(remote??IPAddress.None))throw new NodeApiException(400,"Требуется HTTPS.");
        if(input.Code is null||!Regex.IsMatch(input.Code,@"^[0-9A-Fa-f]{64}$")||
           !Regex.IsMatch(input.NodeId??"",@"^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,127}$")||!Text(input.DisplayName))
            throw new NodeApiException(400,"Некорректные данные подключения.");
        await _enrollment.WaitAsync(ct);
        try
        {
            var all=await state.Data.ListAsync<SetupInvitation>(5000,ct);
            var hashed=Hash(input.Code.ToUpperInvariant());
            var ticket=all.FirstOrDefault(x=>CryptographicOperations.FixedTimeEquals(Convert.FromHexString(x.Hash),Convert.FromHexString(hashed)));
            if(ticket is null||ticket.UsedAt is not null||ticket.ExpiresAt<=now||ticket.ChildKind!=input.ChildKind)
            {
                _rejections.AddOrUpdate(address,_=>(1,now.AddMinutes(15)),(_,value)=>
                    value.Until<now?(1,now.AddMinutes(15)):(value.Count+1,value.Until));
                throw new NodeApiException(401,"Invitation expired or invalid");
            }
            ticket.UsedAt=now;ticket.ConnectedNodeId=input.NodeId;
            await state.Data.PutAsync(ticket,ct);state.Log(input.NodeId,"setup.enroll",ticket.ChildKind,true,ticket.Id.ToString());
            return new{accepted=true,parentKind=node.Kind.ToString(),parentNodeId=node.NodeId,
                childNodeId=input.NodeId,identityProvisioning="required-separately"};
        }
        finally{_enrollment.Release();}
    }
}
public static class SetupDependencyInjection
{
    public static IServiceCollection AddSetup(this IServiceCollection services)
    {
        services.AddSingleton<ISetupAccessPolicy,SetupAccessPolicy>();
        services.AddSingleton<ISetupService,SetupService>();return services;
    }
}
