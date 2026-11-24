using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orchestra/control")]
public sealed class OrchestraControlController(IOrchestraControlProxy proxy) : ControllerBase
{
    [HttpGet("global/overview")]
    public async Task<IActionResult> Overview(CancellationToken ct)
    {
        if(!EcosystemHierarchy.IsDeveloper(User))return Forbid();
        var bearer=Request.Headers.Authorization.ToString();
        if(!bearer.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase))return Unauthorized();
        try
        {
            var response=await proxy.GlobalOverview(bearer,ct);
            return new ContentResult{Content=response.Json,ContentType="application/json",StatusCode=response.StatusCode};
        }
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
}
