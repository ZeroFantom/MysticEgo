using System.Collections.Concurrent;

namespace Models.Commands.SignalR.Hub;

/// <summary>
/// In-memory реализация. Работает с IClientRegistry,
/// чтобы определять онлайн-статус.
/// </summary>
public sealed class InMemoryCapabilityStore : ICapabilityStore
{
    private readonly IClientRegistry _registry;

    // clientId → (commandId → capability)
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<CommandId, CapabilityDto>> _byClient = new();

    public InMemoryCapabilityStore(IClientRegistry registry)
        => _registry = registry ?? throw new ArgumentNullException(nameof(registry));

    public Task ReplaceAsync(
        Guid clientId, IReadOnlyList<CapabilityDto> caps, CancellationToken ct = default)
    {
        var dict = new ConcurrentDictionary<CommandId, CapabilityDto>();

        if (caps is not null)
        {
            foreach (var cap in caps)
            {
                if (cap is null) continue;
                if (cap.Id.IsEmpty) continue;

                dict[cap.Id] = cap;
            }
        }

        _byClient[clientId] = dict;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CapabilityDto>> GetAsync(
        Guid clientId, CancellationToken ct = default)
    {
        if (!_byClient.TryGetValue(clientId, out var dict))
            return Task.FromResult<IReadOnlyList<CapabilityDto>>(Array.Empty<CapabilityDto>());

        IReadOnlyList<CapabilityDto> list = dict.Values
            .OrderBy(c => c.Id)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<IReadOnlyList<CapabilityDto>> GetAllAsync(CancellationToken ct = default)
    {
        var bag = new List<CapabilityDto>();
        var seen = new HashSet<CommandId>();

        foreach (var (_, dict) in _byClient)
        {
            foreach (var cap in dict.Values)
            {
                if (seen.Add(cap.Id))
                    bag.Add(cap);
            }
        }

        bag.Sort((a, b) => a.Id.CompareTo(b.Id));
        return Task.FromResult<IReadOnlyList<CapabilityDto>>(bag);
    }

    public async Task<ClientRecord?> FindClientAsync(
        CommandId commandId, CancellationToken ct = default)
    {
        var all = await FindAllClientsAsync(commandId, ct).ConfigureAwait(false);
        return all.FirstOrDefault();
    }

    public async Task<IReadOnlyList<ClientRecord>> FindAllClientsAsync(
        CommandId commandId, CancellationToken ct = default)
    {
        if (commandId.IsEmpty)
            return Array.Empty<ClientRecord>();

        var result = new List<ClientRecord>();

        foreach (var (clientId, dict) in _byClient)
        {
            if (!dict.ContainsKey(commandId)) continue;

            var client = await _registry.GetByIdAsync(clientId, ct).ConfigureAwait(false);
            if (client is not null && client.Online)
                result.Add(client);
        }

        return result;
    }

    public Task RemoveAsync(Guid clientId, CancellationToken ct = default)
    {
        _byClient.TryRemove(clientId, out _);
        return Task.CompletedTask;
    }
}