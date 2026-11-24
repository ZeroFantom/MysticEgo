using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Hub;

/// <summary>
/// Push-уведомления в админку. Реализация через IHubContext —
/// не привязана к конкретному типу AdminHub, работает с любым,
/// реализующим IAdminClient.
///
/// Все методы безопасные — ошибки логируются, не летят наружу.
/// </summary>
public sealed class AdminNotifier<THub> : IAdminNotifier
    where THub : Hub<IAdminClient>
{
    private readonly IHubContext<THub, IAdminClient> _hub;
    private readonly ILogger<AdminNotifier<THub>> _log;

    public AdminNotifier(
        IHubContext<THub, IAdminClient> hub,
        ILogger<AdminNotifier<THub>> log)
    {
        _hub = hub ?? throw new ArgumentNullException(nameof(hub));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public Task ClientConnectedAsync(ClientSummaryDto summary, CancellationToken ct = default)
        => SafeSendAsync(client => client.ClientConnected(summary),
            nameof(ClientConnectedAsync));

    public Task ClientDisconnectedAsync(Guid clientId, CancellationToken ct = default)
        => SafeSendAsync(client => client.ClientDisconnected(clientId),
            nameof(ClientDisconnectedAsync));

    public Task CapabilitiesUpdatedAsync(
        Guid clientId, int capabilityCount, CancellationToken ct = default)
        => SafeSendAsync(client => client.CapabilitiesUpdated(clientId, capabilityCount),
            nameof(CapabilitiesUpdatedAsync));

    public Task ExecutionCompletedAsync(
        CommandExecutionResult result, CancellationToken ct = default)
        => SafeSendAsync(client => client.ExecutionCompleted(result),
            nameof(ExecutionCompletedAsync));

    public Task ProgressReportedAsync(
        CommandProgress progress, CancellationToken ct = default)
        => SafeSendAsync(client => client.ProgressReported(progress),
            nameof(ProgressReportedAsync));

    private async Task SafeSendAsync(Func<IAdminClient, Task> action, string methodName)
    {
        try
        {
            await action(_hub.Clients.All).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "AdminNotifier.{Method} failed", methodName);
        }
    }
}