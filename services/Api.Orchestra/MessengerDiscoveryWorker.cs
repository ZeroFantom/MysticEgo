using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MysticEgo.Distributed;
using MysticEgo.Soul;
using Models.Autorization.Services;

namespace MysticEgo.Api.Orchestra;

/// <summary>Opt-in, single sequential worker for read-only messenger discovery.
/// No dialogue write or inference while canonical simulation is absent.
/// Only Auth holds provider credentials and selects the private destination.</summary>
public sealed class MessengerDiscoveryWorker(IConfiguration configuration,NodeOptions node,
    IdentityTokenSource identities,WorldPresenceFeedReader reader):BackgroundService
{
    private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false})
        {Timeout=TimeSpan.FromSeconds(5),MaxResponseContentBufferSize=65536};

    private Uri? Endpoint(string action)
    {
        if(!Uri.TryCreate(node.AuthUrl,UriKind.Absolute,out var origin)||origin.UserInfo.Length!=0||
            origin.Query.Length!=0||origin.Fragment.Length!=0||origin.AbsolutePath!="/"||
            !(origin.Scheme=="https"||origin.Scheme=="http"&&origin.IsLoopback))return null;
        return new(origin,"/api/auth/messenger/internal/world/"+action);
    }
    private async Task<HttpResponseMessage> Call(string action,object? body,CancellationToken ct)
    {
        var uri=Endpoint(action)??throw new InvalidOperationException("Invalid Auth origin.");
        using var request=new HttpRequestMessage(HttpMethod.Post,uri);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",await identities.GetAsync(ct));
        if(body is not null)request.Content=JsonContent.Create(body);
        return await http.SendAsync(request,ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while(!stoppingToken.IsCancellationRequested)
        {
            if(configuration.GetValue<bool>("Messenger:DiscoveryWorker:Enabled"))
            {
                try{await Step(stoppingToken);}
                catch(Exception ex) when(ex is HttpRequestException or IOException or System.Text.Json.JsonException or
                    InvalidOperationException or ArgumentException||ex is OperationCanceledException&&!stoppingToken.IsCancellationRequested)
                {
                    // No exception text, command, provider subject or tokens in worker logs.
                }
            }
            await Task.Delay(TimeSpan.FromSeconds(3),stoppingToken);
        }
    }

    private async Task Step(CancellationToken ct)
    {
        using var claim=await Call("claim",null,ct);
        if(claim.StatusCode!=HttpStatusCode.OK)return;
        var lease=await claim.Content.ReadFromJsonAsync<MessengerWorldHandoff.Lease>(cancellationToken:ct);
        if(lease is null||lease.Id==Guid.Empty||lease.LeaseId==Guid.Empty||lease.UserId==Guid.Empty||
            lease.Command is null||!MessengerWorldHandoff.IsWorldCommand(lease.Command)||
            lease.LeaseUntilUtc<=DateTime.UtcNow)return;
        var key=new{lease.Id,lease.LeaseId};
        using var verified=await Call("validate",key,ct);
        if(verified.StatusCode!=HttpStatusCode.NoContent)return;
        var start=DateTimeOffset.UtcNow;
        var view=lease.Command is "/nearby" or "/friends"?
            await reader.ReadConfigured(configuration["WorldAuthority:DatabasePath"],configuration["MiniApp:PresenceFeedPath"],lease.UserId,ct):new WorldPresenceRead(false,null,0);
        var until=start.AddSeconds(view.FreshForSeconds);
        var available=view.SnapshotAvailable&&until>DateTimeOffset.UtcNow;
        IReadOnlyList<SpiritualPublicContact> contacts=available?
            (lease.Command=="/nearby"?view.Presence?.Nearby:view.Presence?.Friends)??[]:[];
        if(!configuration.GetValue<bool>("Messenger:DiscoveryWorker:Enabled"))return;
        using var sent=await Call("discovery-reply",new{lease.Id,lease.LeaseId,available,
            validUntilUtc=available?(DateTimeOffset?)until:null,contacts},ct);
        // Auth atomically consumes the lease before attempting a provider send; never retry here.
    }

    public override void Dispose(){http.Dispose();base.Dispose();}
}
