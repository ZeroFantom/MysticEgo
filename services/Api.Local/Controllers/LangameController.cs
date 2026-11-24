using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Local.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/plugins/langame")]
public sealed class LangameController(ILangameService service) : ControllerBase
{
    private string? Actor()=>User.Identity?.IsAuthenticated==true&&User.FindFirst("token_use")?.Value!="device"?User.Identity.Name??User.FindFirst(ClaimTypes.NameIdentifier)?.Value:null;
    [HttpGet("status")]
    public IActionResult Status()=>Actor() is { } user?Ok(service.Status(user)):Forbid();
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LangameLoginInput input,CancellationToken ct)
    {
        if(Actor() is not { } user)return Forbid();
        try{return Ok(await service.Login(user,input,ct));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpPost("logout")]
    public IActionResult Logout()=>Actor() is { } user?Ok(service.Logout(user)):Forbid();
}
