using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MysticEgo.Capacity;
using MysticEgo.Deployment;

namespace MysticEgo.Distributed;

/// <summary>Publish sanitized resource measurements using the existing enrolled device identity.
/// A second explicitly configured HTTPS collector can remain reachable during an Orchestra outage.</summary>
public sealed class CapacityPublisher(NodeOptions node, ServerTelemetry telemetry, IdentityTokenSource identity,
    IHttpClientFactory clients,IConfiguration config,ILogger<CapacityPublisher> log):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(10));
        while(await timer.WaitForNextTickAsync(ct))
        {
            if(!Guid.TryParse(node.IdentityId,out var id)||telemetry.History().LastOrDefault() is not { } sample)continue;
            var targets=new[]{node.OrchestraUrl,config["Capacity:CollectorUrl"]}.Where(s=>!string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal).Where(s=>Uri.TryCreate(s,UriKind.Absolute,out var u)&&
                    u.Scheme=="https"&&string.IsNullOrEmpty(u.UserInfo)&&string.IsNullOrEmpty(u.Query)&&string.IsNullOrEmpty(u.Fragment)).ToArray();
            if(targets.Length==0)continue;
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(4));
                ModelPool[] pools=[];
                try
                {
                    using var local=clients.CreateClient("mysticego-logged-api");
                    var model=await LocalModels.Ready(local,node.Kind.ToString(),config["LmStudio:VerifiedModelFile"],timeout.Token);
                    if(model is not null)pools=[new("local-model",model.Id,"local",0,0,true)]; // Slot count not measured by discovery.
                }
                catch(Exception ex) when(ex is HttpRequestException or IOException or OperationCanceledException or System.Text.Json.JsonException or InvalidOperationException or KeyNotFoundException){}
                var row=new CapacitySample(id.ToString("D"),node.Kind.ToString(),sample.At,sample.HostCpuPercent,
                    sample.HostMemoryTotalBytes is >0&&sample.HostMemoryUsedBytes is long used?used*100.0/sample.HostMemoryTotalBytes.Value:null,
                    sample.RxBytesPerSecond,sample.TxBytesPerSecond,null,pools,null,null);
                var bearer=await identity.GetAsync(ct);
                await Task.WhenAll(targets.Select(async target=>
                {
                    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(5));
                    try
                    {
                        using var client=clients.CreateClient("mysticego-logged-api");
                        using var request=new HttpRequestMessage(HttpMethod.Post,target!.TrimEnd('/')+"/api/v1/stations/capacity");
                        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
                        request.Content=JsonContent.Create(row);
                        using var result=await client.SendAsync(request,deadline.Token);
                        result.EnsureSuccessStatusCode();
                    }
                    catch(Exception ex) when(ex is HttpRequestException or OperationCanceledException)
                    {if(!ct.IsCancellationRequested)log.LogDebug("Capacity collector unavailable: {Type}",ex.GetType().Name);}
                }));
            }
            catch(Exception ex) when(ex is HttpRequestException or InvalidOperationException or OperationCanceledException)
            {if(!ct.IsCancellationRequested)log.LogDebug("Capacity publication unavailable: {Type}",ex.GetType().Name);}
        }
    }
}
