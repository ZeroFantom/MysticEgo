namespace Models.Commands.SignalR.Hub;

public interface IExecutionDispatcher
{
    Task<CommandExecutionResult> DispatchAsync(
        Guid clientId,
        CommandExecutionRequest request,
        TimeSpan timeout,
        IProgress<CommandProgress>? progress = null,
        CancellationToken ct = default);

    Task<CommandExecutionResult> DispatchToAnyAsync(
        CommandExecutionRequest request,
        TimeSpan timeout,
        IProgress<CommandProgress>? progress = null,
        CancellationToken ct = default);

    bool CompleteFrom(Guid clientId, string connectionId, CommandExecutionResult result);

    /// <summary>Приём прогресса от клиента. Вызывается хабом.</summary>
    bool ReportFrom(Guid clientId, string connectionId, CommandProgress progress);

    int PendingCount { get; }
}