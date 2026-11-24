using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Routing;
using Models.Commands;
using Models.Data;

namespace MysticEgo.Distributed;

public sealed record ApiMethod(string Id, string Description, string Scope, string Permission, bool RequiresConfirmation, string? Path);

[Entity("mysticego.api.catalog", 1)]
public sealed class ApiMethodSnapshot
{
    [EntityKey] public Guid Id { get; set; }
    public string Revision { get; set; } = "";
    public ApiMethod[] Methods { get; set; } = [];
}

public sealed class ApiMethodCatalog(IEnumerable<EndpointDataSource> endpoints, NodeState state)
{
    private static readonly Guid Id = Guid.Parse("422f2cd6-8747-4318-8a16-6cc46b1400f0");
    private readonly SemaphoreSlim sync = new(1, 1);
    private ApiMethod[]? cached;

    public async Task<ApiMethod[]> Read(CancellationToken ct)
    {
        await sync.WaitAsync(ct);
        try
        {
            if (cached is not null) return cached;
            var found = endpoints.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>()
                .Select(route => (route.RoutePattern.RawText, Capability: route.Metadata.GetMetadata<ApiCapabilityAttribute>()))
                .Where(entry => entry.Capability is not null)
                .Select(entry =>
                {
                    var capability = entry.Capability!;
                    return new ApiMethod(capability.Id, capability.Description, capability.Scope,
                        capability.Permission, capability.RequiresConfirmation, entry.RawText);
                })
                .OrderBy(method => method.Id).ThenBy(method => method.Path).ToArray();
            var revision = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(found)));
            var saved = await state.Data.GetAsync<ApiMethodSnapshot>(Id, ct);
            if (saved?.Revision != revision)
            {
                saved = new ApiMethodSnapshot { Id = Id, Revision = revision, Methods = found };
                await state.Data.PutAsync(saved, ct);
            }
            return cached = saved.Methods;
        }
        finally { sync.Release(); }
    }
}
