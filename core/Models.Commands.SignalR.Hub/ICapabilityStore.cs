namespace Models.Commands.SignalR.Hub;

/// <summary>
/// Хранилище capabilities. Отвечает на два вопроса:
///   • какие команды есть у конкретного клиента;
///   • какой клиент умеет команду X.
/// </summary>
public interface ICapabilityStore
{
    /// <summary>Атомарно заменить каталог клиента.</summary>
    Task ReplaceAsync(Guid clientId, IReadOnlyList<CapabilityDto> caps, CancellationToken ct = default);

    /// <summary>Каталог конкретного клиента.</summary>
    Task<IReadOnlyList<CapabilityDto>> GetAsync(Guid clientId, CancellationToken ct = default);

    /// <summary>Все capabilities всех клиентов.</summary>
    Task<IReadOnlyList<CapabilityDto>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Найти онлайн-клиента, который умеет команду.</summary>
    Task<ClientRecord?> FindClientAsync(CommandId commandId, CancellationToken ct = default);

    /// <summary>Найти всех клиентов с этой командой.</summary>
    Task<IReadOnlyList<ClientRecord>> FindAllClientsAsync(CommandId commandId, CancellationToken ct = default);

    /// <summary>Убрать каталог клиента (при disconnect).</summary>
    Task RemoveAsync(Guid clientId, CancellationToken ct = default);
}