using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController]
public sealed class PanelDirectoryController(IConfiguration config, ManagedNetwork network, DevelopmentTeam team) : ControllerBase
{
    private const int Limit = 500;

    [HttpGet("api/v1/public/panel-entrypoints"), AllowAnonymous]
    public IActionResult PublicEntries()
    {
        var entries = config.GetSection("PanelDirectory:Public").GetChildren().Take(50)
            .Select(row => Entry(row["Kind"], row["Name"] ?? row.Key, row["Url"]))
            .OfType<PanelEntry>().DistinctBy(row => (row.Kind, row.Url));
        return Ok(entries);
    }

    [HttpGet("api/v1/managed/panel-entrypoints"), Authorize(Policy = "operator")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ManagedEntries()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) return Forbid();
        var developer = team.IsOwner(User) || HttpContext.Items.ContainsKey(typeof(DevelopmentTeam));
        var entries = network.Nodes()
            .Where(node => (node.Kind is "global" or "agent") && node.Enabled && network.CanRead(node, actor, developer))
            .Select(node => Entry(node.Kind, node.Name, node.PublicUrl, managed: true))
            .OfType<PanelEntry>().DistinctBy(row => (row.Kind, row.Url))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase).Take(Limit + 1).ToArray();
        return Ok(new { entries = entries.Take(Limit), truncated = entries.Length > Limit });
    }

    private static PanelEntry? Entry(string? kind, string? name, string? address, bool managed = false)
    {
        if (kind is not ("audit" or "global" or "agent") || string.IsNullOrWhiteSpace(name) ||
            address is null || address.Length > 300 || !Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
            uri.Host.Length == 0 || uri.IsLoopback || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 ||
            uri.Scheme != "https" ||
            (managed && uri.AbsolutePath != "/") || (!managed && kind != "audit" && uri.AbsolutePath != "/") ||
            (!managed && kind == "audit" && uri.AbsolutePath is not ("/" or "/audit-portal" or "/audit-portal/")))
            return null;
        return new PanelEntry(kind, name.Length > 80 ? name[..80] : name, uri.AbsoluteUri);
    }

    private sealed record PanelEntry(string Kind, string Name, string Url);
}
