namespace Models.Commands.SignalR;

/// <summary>
/// Что клиент может вызвать на сервере.
/// </summary>
public interface IAgentHub
{
    Task<CapabilitySyncResponse> SyncCapabilities(CapabilitySyncRequest request);
    Task Heartbeat(HeartbeatReport report);
    Task ReportExecution(CommandExecutionResult result);

    /// <summary>Клиент отчитывается о прогрессе выполнения.</summary>
    Task ReportProgress(CommandProgress progress);
}