using System.Collections.Concurrent;

namespace Models.Commands.SignalR.Hub;

/// <summary>
/// In-memory реализация. Потокобезопасная. Не персистентная —
/// при рестарте сервера реестр пуст.
/// </summary>
public sealed class InMemoryClientRegistry : IClientRegistry
{
    private readonly ConcurrentDictionary<Guid, ClientRecord> _byId = new();
    private readonly ConcurrentDictionary<string, Guid> _byConnection = new();

    public Task<ClientRecord> RegisterAsync(ClientRecord record, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        // Если клиент с таким ID уже был — отвязываем старый connection
        if (_byId.TryGetValue(record.ClientId, out var existing) &&
            !string.Equals(existing.ConnectionId, record.ConnectionId, StringComparison.Ordinal))
        {
            _byConnection.TryRemove(existing.ConnectionId, out _);
        }

        _byId[record.ClientId] = record;
        _byConnection[record.ConnectionId] = record.ClientId;

        return Task.FromResult(record);
    }

    public Task UnregisterAsync(string connectionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(connectionId)) return Task.CompletedTask;

        if (!_byConnection.TryRemove(connectionId, out var clientId))
            return Task.CompletedTask;

        if (_byId.TryGetValue(clientId, out var current) &&
            string.Equals(current.ConnectionId, connectionId, StringComparison.Ordinal))
        {
            _byId[clientId] = current with
            {
                Online = false,
                LastSeenAt = DateTime.UtcNow,
            };
        }

        return Task.CompletedTask;
    }

    public Task<ClientRecord?> GetByIdAsync(Guid clientId, CancellationToken ct = default)
    {
        _byId.TryGetValue(clientId, out var record);
        return Task.FromResult(record);
    }

    public Task<ClientRecord?> GetByConnectionAsync(string connectionId, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(connectionId))
            return Task.FromResult<ClientRecord?>(null);

        if (!_byConnection.TryGetValue(connectionId, out var clientId))
            return Task.FromResult<ClientRecord?>(null);

        _byId.TryGetValue(clientId, out var record);
        return Task.FromResult(record);
    }

    public Task<IReadOnlyList<ClientRecord>> ListAsync(CancellationToken ct = default)
    {
        IReadOnlyList<ClientRecord> list = _byId.Values
            .OrderBy(c => c.ClientId)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<int> CountOnlineAsync(CancellationToken ct = default)
    {
        var count = _byId.Values.Count(c => c.Online);
        return Task.FromResult(count);
    }

    public Task TouchAsync(Guid clientId, HeartbeatReport report, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(clientId, out var current))
            return Task.CompletedTask;

        _byId[clientId] = current with
        {
            LastSeenAt = DateTime.UtcNow,
            UptimeSeconds = report.UptimeSeconds,
            RunningCommands = report.RunningCommands,
            Online = true,
        };

        return Task.CompletedTask;
    }

    public Task UpdateCapabilityCountAsync(Guid clientId, int count, CancellationToken ct = default)
    {
        if (!_byId.TryGetValue(clientId, out var current))
            return Task.CompletedTask;

        _byId[clientId] = current with { CapabilityCount = count };
        return Task.CompletedTask;
    }
}