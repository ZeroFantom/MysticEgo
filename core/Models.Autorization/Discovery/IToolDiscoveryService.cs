using Models.Autorization.Contracts.Responses;

namespace Models.Autorization.Discovery;

public interface IToolDiscoveryService
{
    Task<DiscoverySyncResult> SyncAsync(CancellationToken ct = default);
}