namespace Models.Commands.SignalR;

/// <summary>
/// Что сервер может вызвать на клиенте.
/// </summary>
public interface IAgentClient
{
    Task<CommandExecutionResult> ExecuteCommand(CommandExecutionRequest request);
    Task RequestCapabilities();
    Task UpdateConfiguration(ConfigurationUpdate update);
}