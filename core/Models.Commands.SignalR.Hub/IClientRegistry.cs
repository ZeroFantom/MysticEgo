namespace Models.Commands.SignalR.Hub;

/// <summary>
/// Реестр подключённых клиентов. In-memory по умолчанию.
/// Замени реализацией, которая пишет в БД, если нужна персистентность.
/// </summary>
public interface IClientRegistry
{
    Task<ClientRecord> RegisterAsync(ClientRecord record, CancellationToken ct = default);
    Task UnregisterAsync(string connectionId, CancellationToken ct = default);

    Task<ClientRecord?> GetByIdAsync(Guid clientId, CancellationToken ct = default);
    Task<ClientRecord?> GetByConnectionAsync(string connectionId, CancellationToken ct = default);

    Task<IReadOnlyList<ClientRecord>> ListAsync(CancellationToken ct = default);
    Task<int> CountOnlineAsync(CancellationToken ct = default);

    Task TouchAsync(Guid clientId, HeartbeatReport report, CancellationToken ct = default);
    Task UpdateCapabilityCountAsync(Guid clientId, int count, CancellationToken ct = default);
}