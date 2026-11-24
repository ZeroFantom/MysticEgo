using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Agent.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/network-repair")]
public sealed class NetworkRepairController(INetworkRepairService repair) : ControllerBase
{
    private bool Allowed()
    {
        if (User.Identity?.IsAuthenticated != true || User.FindFirst("token_use")?.Value == "device") return false;
        return User.IsInRole("system.developer") || User.IsInRole("local.admin") || User.IsInRole("global.manager") ||
               User.HasClaim("permission", "system.bypass") || User.HasClaim("permission", "network.repair");
    }

    [HttpGet("status")]
    public IActionResult Status() => Allowed() ? Ok(repair.Status()) : Forbid();

    [HttpPost("actions")]
    public async Task<IActionResult> Execute([FromBody] RepairOperation request, CancellationToken ct)
    {
        if (!Allowed()) return Forbid();
        var result = await repair.ExecuteAsync(request, ct);
        return result.Success ? Ok(result) : StatusCode(409, result);
    }
}
