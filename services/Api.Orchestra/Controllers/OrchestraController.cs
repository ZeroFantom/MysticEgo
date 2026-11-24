using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orchestra")]
public sealed class OrchestraController(NodeState state) : ControllerBase
{
    private bool Developer() => EcosystemHierarchy.IsDeveloper(User);

    [HttpGet("dashboard")]
    public IActionResult Dashboard() => Developer() ? Ok(new
    {
        title = "Orchestra · центр экосистемы", node = state.Options.NodeId,
        topology = state.Nodes.Values.OrderBy(x => x.Kind).ToArray(),
        audited = state.Audit.Count,
        notice = "Глобальная видимость не означает безусловное исполнение опасных команд."
    }) : Forbid();

    [HttpGet("catalog")]
    public IActionResult Catalog() => Developer() ? Ok(new
    {
        services = new[] { "Auth", "Orchestra", "Global", "Local", "Agent", "AuditOrchestra" },
        priorities = EcosystemHierarchy.Levels.ToDictionary(x => x.Key.ToString(), x => x.Value)
    }) : Forbid();
}
