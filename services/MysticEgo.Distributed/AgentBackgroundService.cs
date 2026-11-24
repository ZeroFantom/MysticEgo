using System.Reflection;
using Models.Commands.Scanner;
using Models.Commands.SignalR.Clients;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace MysticEgo.Distributed;
public sealed class AgentBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly NodeOptions _options;
    private readonly ILogger<AgentBackgroundService> _log;
    private readonly IdentityTokenSource _tokens;
    public AgentBackgroundService(IServiceProvider services,NodeOptions options,ILogger<AgentBackgroundService> log,IdentityTokenSource tokens) { _services=services;_options=options;_log=log;_tokens=tokens; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!Guid.TryParse(_options.IdentityId,out var id)) throw new InvalidOperationException("Перед запуском агента необходимо зарегистрировать Node:IdentityId");
        var control = _options.Managed && _options.Kind == NodeKind.Agent ? _options.OrchestraUrl : null;
        var urls=new[]{_options.LocalUrl,_options.GlobalUrl,control}.Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
        var clients=new List<ClusterAgentClient>();
        try
        {
            foreach(var url in urls)
            {
                if (!Uri.TryCreate(url,UriKind.Absolute,out _)) continue;
                var hubUrl=url!.TrimEnd('/')+"/hub/agents";
                var settings=new AgentHubClientOptions {HubUrl=hubUrl,AccessTokenProvider=_tokens.GetAsync,ClientId=id,CommandAssemblies=new[]{typeof(SystemInfoCommand).Assembly}};
                settings.AdditionalHeaders["X-Agent-Id"]=id.ToString();
                clients.Add(new ClusterAgentClient(settings,_services.GetRequiredService<ICapabilityScanner>(),_services.GetRequiredService<ICommandInvoker>(),_services.GetRequiredService<ILogger<ClusterAgentClient>>()));
            }
            var started = new HashSet<ClusterAgentClient>();
            while (!stoppingToken.IsCancellationRequested)
            {
                foreach (var client in clients.Where(c=>!started.Contains(c)))
                {
                    try { await client.StartAsync(stoppingToken);started.Add(client); }
                    catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
                    catch(Exception ex) { _log.LogWarning(ex,"Agent connection failed; retrying"); }
                }
                await Task.Delay(TimeSpan.FromSeconds(15),stoppingToken);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        finally { foreach(var client in clients) await client.DisposeAsync(); }
    }
}
