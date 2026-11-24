namespace Models.Commands.SignalR;

/// <summary>
/// Что сервер пушит в админку.
/// Реализуется хабом через Hub&lt;IAdminClient&gt;.
/// </summary>
public interface IAdminClient
{
    /// <summary>Клиент подключился.</summary>
    Task ClientConnected(ClientSummaryDto summary);

    /// <summary>Клиент отключился.</summary>
    Task ClientDisconnected(Guid clientId);

    /// <summary>Клиент обновил capabilities.</summary>
    Task CapabilitiesUpdated(Guid clientId, int capabilityCount);

    /// <summary>Одна команда завершилась.</summary>
    Task ExecutionCompleted(CommandExecutionResult result);

    /// <summary>Прогресс выполнения команды.</summary>
    Task ProgressReported(CommandProgress progress);
}