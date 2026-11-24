using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MysticEgo.Distributed;

/// <summary>
/// Isolated observer for OPTIONAL peer services. A peer outage never changes the
/// process liveness/readiness or blocks unrelated endpoints. All calls are bounded.
/// </summary>
public sealed record CapabilityHealth(string Name, string State, DateTimeOffset CheckedAt,
    int? HttpStatus, long? LatencyMs, string? Reason);

public sealed class OptionalDependencyState
{
    private readonly ConcurrentDictionary<string,CapabilityHealth> _health = new(StringComparer.OrdinalIgnoreCase);
    public void Set(string name,string state,int? status=null,long? elapsed=null,string? reason=null) =>
        _health[name]=new CapabilityHealth(name,state,DateTimeOffset.UtcNow,status,elapsed,reason);
    public CapabilityHealth[] Snapshot() => _health.Values.OrderBy(x=>x.Name).ToArray();
}

public sealed class OptionalDependencyMonitor : BackgroundService
{
    private readonly NodeOptions _options;
    private readonly OptionalDependencyState _state;
    private readonly IHttpClientFactory _clients;
    private readonly ILogger<OptionalDependencyMonitor> _logger;
    public OptionalDependencyMonitor(NodeOptions options,OptionalDependencyState state,
        IHttpClientFactory clients,ILogger<OptionalDependencyMonitor> logger)
    { _options=options;_state=state;_clients=clients;_logger=logger; }

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        // Never throw from this loop: optional diagnostics must not kill the host.
        while(!stop.IsCancellationRequested)
        {
            try
            {
                var targets=new (string Name,string? Url,string Path)[]{
                    ("auth",_options.AuthUrl,"/health"),
                    ("orchestra",_options.OrchestraUrl,"/health/live"),
                    ("global",_options.GlobalUrl,"/health/live"),
                    ("local",_options.LocalUrl,"/health/live")};
                await Task.WhenAll(targets.Where(x=>!string.IsNullOrWhiteSpace(x.Url))
                    .Select(x=>Probe(x.Name,x.Url!,x.Path,stop)));
                await Task.Delay(TimeSpan.FromSeconds(20),stop);
            }
            catch(OperationCanceledException) when(stop.IsCancellationRequested){break;}
            catch(Exception e)
            {
                _logger.LogWarning(e,"Optional dependency monitor cycle failed");
                try{await Task.Delay(TimeSpan.FromSeconds(5),stop);}
                catch(OperationCanceledException){break;}
            }
        }
    }
    private async Task Probe(string name,string baseUrl,string path,CancellationToken stop)
    {
        var clock=Stopwatch.StartNew();
        try
        {
            if(!Uri.TryCreate(baseUrl.TrimEnd('/')+path,UriKind.Absolute,out var uri) ||
               uri.Scheme is not ("http" or "https"))
            { _state.Set(name,"degraded",reason:"invalid-upstream-url");return; }
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using var client=_clients.CreateClient("optional-peers");
            using var response=await client.GetAsync(uri,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            _state.Set(name,response.IsSuccessStatusCode?"available":"degraded",
                (int)response.StatusCode,clock.ElapsedMilliseconds,
                response.IsSuccessStatusCode?null:"upstream-http-error");
        }
        catch(OperationCanceledException) when(stop.IsCancellationRequested){}
        catch(Exception e) when(e is HttpRequestException or OperationCanceledException or UriFormatException)
        { _state.Set(name,"unavailable",elapsed:clock.ElapsedMilliseconds,reason:e is OperationCanceledException?"timeout":"connection-failed"); }
    }
}

public static class OptionalServices
{
    public static IServiceCollection AddOptionalServices(this IServiceCollection services)
    {
        services.AddSingleton<OptionalDependencyState>();
        services.AddHttpClient("optional-peers",c=>c.Timeout=TimeSpan.FromSeconds(4))
            .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false});
        services.AddHostedService<OptionalDependencyMonitor>();
        return services;
    }
}
