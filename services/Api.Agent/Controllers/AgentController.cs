using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Agent.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/agent")]
public sealed class AgentController(AgentUpdateState state, AgentUpdateWorker worker) : ControllerBase
{
    private bool Human() => User.Identity?.IsAuthenticated == true && User.FindFirst("token_use")?.Value != "device";

    [HttpGet("system")]
    public IActionResult System() => Human()
        ? Ok(new { host = Environment.MachineName, os = Environment.OSVersion.ToString(), uptimeMs = Environment.TickCount64 }) : Forbid();

    [HttpGet("updates/status")]
    public IActionResult UpdateStatus() => Human() ? Ok(state) : Forbid();

    [HttpPost("updates/check")]
    public async Task<IActionResult> Check(CancellationToken ct)
    {
        if (!Human()) return Forbid();
        try { await worker.CheckAsync(ct); return Ok(new { started = true }); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return Problem("Не удалось выполнить проверку обновления.", statusCode: 503); }
    }
}
