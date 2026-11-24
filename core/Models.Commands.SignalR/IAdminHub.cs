namespace Models.Commands.SignalR;

/// <summary>
/// Что админка может вызвать на сервере.
/// </summary>
public interface IAdminHub
{
    /// <summary>Запросить список текущих клиентов.</summary>
    Task<IReadOnlyList<ClientSummaryDto>> ListClients();

    /// <summary>Запросить capabilities конкретного клиента.</summary>
    Task<IReadOnlyList<CapabilityDto>> GetCapabilities(Guid clientId);

    /// <summary>Отправить команду клиенту.</summary>
    Task<CommandExecutionResult> ExecuteOnClient(
        Guid clientId, CommandExecutionRequest request);
}