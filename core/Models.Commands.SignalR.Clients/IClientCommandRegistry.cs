namespace Models.Commands.SignalR.Clients;

/// <summary>
/// Клиентский реестр: CommandId → CLR-тип команды.
/// Строится из сборок. Используется для создания экземпляра
/// по пришедшему execution request.
/// </summary>
public interface IClientCommandRegistry
{
    Type? FindType(CommandId id);
    IReadOnlyCollection<CommandId> AllIds { get; }
    void Rescan();
}