namespace Models.Commands.SignalR.Hub;

public interface IAdminNotifier
{
    Task ClientConnectedAsync(ClientSummaryDto summary, CancellationToken ct = default);
    Task ClientDisconnectedAsync(Guid clientId, CancellationToken ct = default);
    Task CapabilitiesUpdatedAsync(Guid clientId, int count, CancellationToken ct = default);
    Task ExecutionCompletedAsync(CommandExecutionResult result, CancellationToken ct = default);

    /// <summary>Прогресс выполнения команды.</summary>
    Task ProgressReportedAsync(CommandProgress progress, CancellationToken ct = default);
}

public sealed class NullAdminNotifier : IAdminNotifier
{
    public static readonly NullAdminNotifier Instance = new();

    public Task ClientConnectedAsync(ClientSummaryDto summary, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ClientDisconnectedAsync(Guid clientId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CapabilitiesUpdatedAsync(Guid clientId, int capabilityCount, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ExecutionCompletedAsync(CommandExecutionResult result, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task ProgressReportedAsync(CommandProgress progress, CancellationToken ct = default)
        => Task.CompletedTask;
}