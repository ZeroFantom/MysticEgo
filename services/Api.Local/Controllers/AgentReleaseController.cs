using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Local.Controllers;
[ApiController]
[Authorize]
[Route("api/v1/updates/agent")]
public sealed class AgentReleaseController(IAgentReleaseService releases) : ControllerBase
{
    private bool CanRead()=>User.Identity?.IsAuthenticated==true&&(User.FindFirst("token_use")?.Value!="device"||User.IsInRole("agent"));
    private bool CanPublish()=>User.Identity?.IsAuthenticated==true&&User.FindFirst("token_use")?.Value!="device"&&(User.IsInRole("system.developer")||User.HasClaim("permission","system.bypass")||User.HasClaim("permission","node.write"));
    [HttpGet("latest")]
    public IActionResult Latest(){if(!CanRead())return Forbid();var path=releases.LatestPath();return path is null?NotFound(new{error="no_release"}):PhysicalFile(path,"application/json");}
    [HttpGet("package")]
    public IActionResult Package([FromQuery] string? version)
    {
        if(!CanRead())return Forbid();
        try{var path=releases.PackagePath(version);return path is null?NotFound():PhysicalFile(path,"application/zip","mysticego-agent.zip");}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpPost]
    [RequestSizeLimit(105_000_000)]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Publish([FromForm] string version,[FromForm(Name="package")] IFormFile package,CancellationToken ct)
    {
        if(!CanPublish())return Forbid();
        if(package is null)return BadRequest(new{error="missing_package"});
        try{return Ok(await releases.Publish(package,version,ct));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
        catch(System.IO.InvalidDataException){return BadRequest(new{error="invalid_zip"});}
    }
}
