using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public sealed class PeerAdmission(NodeOptions node, IdentityTokenSource identity, IHttpClientFactory clients)
{
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> known = new();

    public async Task<bool> Allow(Guid agent, CancellationToken ct)
    {
        if (!node.Managed) return true;
        if (!Guid.TryParse(node.IdentityId, out var own) || !Uri.TryCreate(node.OrchestraUrl, UriKind.Absolute, out var central))
            return false;
        if (known.TryGetValue(agent, out var previous) && previous > DateTimeOffset.UtcNow.AddMinutes(-1)) return true;
        try
        {
            using var wait = CancellationTokenSource.CreateLinkedTokenSource(ct);
            wait.CancelAfter(TimeSpan.FromSeconds(4));
            using var request = new HttpRequestMessage(HttpMethod.Get, central.AbsoluteUri.TrimEnd('/') + $"/api/v1/managed/peers/{own}/{agent}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await identity.GetAsync(wait.Token));
            using var client = clients.CreateClient("mysticego-logged-api");
            using var response = await client.SendAsync(request, wait.Token);
            if (response.IsSuccessStatusCode)
            {
                if (known.Count > 4000) known.Clear();
                known[agent] = DateTimeOffset.UtcNow;
                return true;
            }
            known.TryRemove(agent, out _);
        }
        catch (Exception error) when (error is HttpRequestException or InvalidOperationException ||
            error is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return previous > DateTimeOffset.UtcNow.AddMinutes(-10);
        }
        return false;
    }
}
