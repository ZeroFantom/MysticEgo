using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public static class ClubLan
{
    public static bool PrivateAddress(IPAddress? address)
    {
        if (address is null) return false;
        if (IPAddress.IsLoopback(address)) return true;
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var parts = address.GetAddressBytes();
        return parts.Length == 4
            ? parts[0] == 10 || parts[0] == 172 && parts[1] is >= 16 and <= 31 || parts[0] == 192 && parts[1] == 168
            : parts.Length == 16 && (parts[0] & 0xFE) == 0xFC;
    }

    public static async Task<bool> ClubPresent(NodeOptions options, IHttpClientFactory factory, CancellationToken ct)
    {
        if (!options.Managed || options.Kind != NodeKind.Agent || !Guid.TryParse(options.LocalId, out var local)) return false;
        var parents = new[] { (options.LocalUrl, local.ToString()), (options.GlobalUrl, options.GlobalId) };
        foreach (var (host, id) in parents)
        {
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                using var http = factory.CreateClient("mysticego-logged-api");
                var result = await http.GetFromJsonAsync<JsonElement>(host.TrimEnd('/') + "/health/live", timeout.Token);
                if (result.TryGetProperty("node", out var key) && key.GetString() == id) return true;
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested) { }
        }
        return false;
    }
}
