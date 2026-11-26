using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra.Controllers;

/// <summary>
/// Lists configured public login destinations and the managed destinations visible to the caller.
/// </summary>
[ApiController]
public sealed class PanelDirectoryController(IConfiguration config, ManagedNetwork network, DevelopmentTeam team) : ControllerBase
{
    private const int Limit = 500;

    /// <summary>Returns login destinations explicitly configured for anonymous visitors.</summary>
    [HttpGet("api/v1/public/panel-entrypoints"), AllowAnonymous]
    public IActionResult PublicEntries()
    {
        var entries = config.GetSection("PanelDirectory:Public")
            .GetChildren()
            .Take(50)
            .Select(row => Entry(row["Kind"], row["Name"] ?? row.Key, row["Url"]))
            .OfType<PanelEntry>()
            .DistinctBy(row => (row.Kind, row.Url));
        return Ok(entries);
    }

    /// <summary>Returns a bounded list of enabled panels the current operator can access.</summary>
    [HttpGet("api/v1/managed/panel-entrypoints"), Authorize(Policy = "operator")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult ManagedEntries()
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor))
        {
            return Forbid();
        }

        var developer = team.IsOwner(User) || HttpContext.Items.ContainsKey(typeof(DevelopmentTeam));
        var entries = network.Nodes()
            .Where(node => (node.Kind is "global" or "agent") &&
                node.Enabled && network.CanRead(node, actor, developer))
            .Select(node => Entry(node.Kind, node.Name, node.PublicUrl, managed: true))
            .OfType<PanelEntry>()
            .DistinctBy(row => (row.Kind, row.Url))
            .OrderBy(row => row.Name, StringComparer.OrdinalIgnoreCase)
            .Take(Limit + 1)
            .ToArray();

        // Use one extra entry to detect truncation at the response limit.
        return Ok(new { entries = entries.Take(Limit), truncated = entries.Length > Limit });
    }

    private static PanelEntry? Entry(string? kind, string? name, string? address, bool managed = false)
    {
        if (kind is not ("audit" or "global" or "agent") || string.IsNullOrWhiteSpace(name) ||
            address is null || address.Length > 300 || !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || uri.Host.Length == 0 || uri.IsLoopback ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
        {
            return null;
        }

        // Only an explicitly configured Audit destination may point at the shared portal path.
        var isAuditPortal = !managed && kind == "audit" &&
            uri.AbsolutePath is "/audit-portal" or "/audit-portal/";
        if (uri.AbsolutePath != "/" && !isAuditPortal)
        {
            return null;
        }

        return new PanelEntry(kind, name.Length > 80 ? name[..80] : name, uri.AbsoluteUri);
    }

    private sealed record PanelEntry(string Kind, string Name, string Url);
}
