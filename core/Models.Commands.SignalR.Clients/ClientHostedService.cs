using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Models.Commands.SignalR.Clients;

internal sealed class ClientHostedService<TClient> : IHostedService
    where TClient : AgentHubClientBase
{
    private readonly TClient _client;

    public ClientHostedService(IServiceProvider sp)
        => _client = sp.GetRequiredService<TClient>();

    public Task StartAsync(CancellationToken cancellationToken)
        => _client.StartAsync(cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
        => _client.StopAsync(cancellationToken);
}