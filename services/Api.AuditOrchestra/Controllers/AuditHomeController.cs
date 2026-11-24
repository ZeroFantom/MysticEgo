using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Commands;
using Models.Commands.SignalR.Clients;

namespace MysticEgo.Api.AuditOrchestra.Controllers;

public sealed record SafeHomeCommand(string CommandId);

[ApiController]
[Authorize]
[Route("api/v1/audit-orchestra")]
public sealed class AuditHomeController(ICommandInvoker commands) : ControllerBase
{
    private bool Human() => User.Identity?.IsAuthenticated == true && User.FindFirst("token_use")?.Value != "device";
    private string? Owner()
    {
        var owner = Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");
        return Human() && !string.IsNullOrWhiteSpace(owner) &&
            string.Equals(User.Identity?.Name, owner, StringComparison.Ordinal) ? owner : null;
    }

    [HttpGet("system")]
    public IActionResult System() => Human() ? Ok(new { device = Environment.MachineName, os = Environment.OSVersion.ToString(), uptimeMs = Environment.TickCount64 }) : Forbid();

    [HttpGet("home")]
    public IActionResult Home() => Human()
        ? Ok(new { device = Environment.MachineName, kind = "AuditOrchestra", scope = "home", assistant = "LM Studio", account = "persistent" }) : Forbid();

    [HttpPost("execute")]
    public async Task<IActionResult> Execute([FromBody] SafeHomeCommand input, CancellationToken ct)
    {
        var owner = Owner();
        if (owner is null) return Forbid();
        if (input.CommandId is not ("system.info" or "system.details" or "system.health" or "system.drives" or
            "network.interfaces" or "process.list" or "process.details" or "audio.devices" or "rgb.status"))
            return BadRequest(new { error = "Команда недоступна в безопасном домашнем интерфейсе." });
        var context = new CommandContext { ExecutionId = Guid.NewGuid(), CommandId = CommandId.FromName(input.CommandId), RawArgs = "", CallerLogin = owner };
        return Ok(await commands.InvokeAsync(context, ct));
    }
}
