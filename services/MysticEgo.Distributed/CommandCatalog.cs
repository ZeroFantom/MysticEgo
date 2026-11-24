using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Models.Commands.Scanner;
using Models.Commands.SignalR;
using Models.Data;

namespace MysticEgo.Distributed;

[Entity("mysticego.commands.catalog", 1)]
public sealed class CommandCatalogSnapshot
{
    [EntityKey] public Guid Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public JsonElement Commands { get; set; }
}

public sealed class CommandCatalog(NodeState state, ICapabilityScanner scanner)
{
    private static readonly Guid CatalogId = Guid.Parse("49faaca7-a6b7-4c6f-9950-ea4830981fe1");
    private readonly SemaphoreSlim sync = new(1, 1);
    private JsonElement cached;

    public static object Describe(CapabilityDto capability) => new
    {
        id = capability.Id.Value, capability.DisplayName, capability.Description, capability.Category,
        capability.Version, capability.Parameters, capability.Examples, capability.Tags
    };

    public async Task<JsonElement> Read(CancellationToken ct)
    {
        await sync.WaitAsync(ct);
        try
        {
            if (cached.ValueKind != JsonValueKind.Undefined) return cached;
            var commands = scanner.Scan(typeof(SystemInfoCommand).Assembly).OrderBy(x => x.Id.Value).Select(Describe).ToArray();
            var json = JsonSerializer.SerializeToUtf8Bytes(commands, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            var fingerprint = Convert.ToHexString(SHA256.HashData(json));
            var snapshot = await state.Data.GetAsync<CommandCatalogSnapshot>(CatalogId, ct);
            if (snapshot?.Fingerprint != fingerprint)
            {
                using var document = JsonDocument.Parse(json);
                snapshot = new() { Id = CatalogId, Fingerprint = fingerprint, Commands = document.RootElement.Clone() };
                await state.Data.PutAsync(snapshot, ct);
            }
            cached = snapshot.Commands;
            return cached;
        }
        finally { sync.Release(); }
    }
}

[ApiController, Authorize(Policy = "operator"), Route("api/v1/commands/catalog")]
public sealed class CommandCatalogController(CommandCatalog catalog) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await catalog.Read(ct));
}
