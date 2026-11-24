using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Deployment;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize, Route("api/v1/stations")]
public sealed class StationsController(StationRegistry stations) : ControllerBase
{
    [HttpGet]
    public IActionResult List() => EcosystemHierarchy.IsDeveloper(User) ? Ok(stations.List()) : Forbid();

    [HttpPost]
    public IActionResult Create() => StatusCode(410, new { error = "Выпускайте конфигурации через панель родительского узла." });
    [HttpPut("{id:guid}")]
    public IActionResult Set(Guid id, StationDecision request)
    {
        if (!EcosystemHierarchy.IsDeveloper(User)) return Forbid();
        return stations.SetEnabled(id, request.Enabled) ? NoContent() : NotFound();
    }
    [HttpPost("check-in"), Authorize(Policy="node-peer")]
    public IActionResult CheckIn(StationReport report)
    {
        if (!Guid.TryParse(User.FindFirst("identity_id")?.Value, out var id)) return Forbid();
        var decision = stations.CheckIn(id, report);
        return decision == null ? Forbid() : Ok(decision);
    }
}
