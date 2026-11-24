using Models.Commands;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace MysticEgo.Distributed;

[ApiController]
[Authorize]
[Route("api/v1/ecosystem")]
public sealed class EcosystemController(IEtherService ethers,NodeOptions options,ApiMethodCatalog catalog) : ControllerBase
{
    private string? Actor() => User.Identity?.IsAuthenticated==true && User.FindFirst("token_use")?.Value!="device"
        ? User.Identity.Name??User.FindFirst(ClaimTypes.NameIdentifier)?.Value:null;
    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(CancellationToken ct)
    {
        if (Actor() is null) return Forbid();
        var methods = await catalog.Read(ct);
        return Ok(methods.Where(method => string.IsNullOrEmpty(method.Permission) ||
            User.HasClaim("permission", method.Permission) || EcosystemHierarchy.IsDeveloper(User)));
    }
    [HttpGet("hierarchy")]
    [ApiCapability("ecosystem.hierarchy","Иерархия узлов","all")]
    public IActionResult Hierarchy() => Actor() is null ? Forbid() : Ok(new
    {
        current=EcosystemHierarchy.Description(options.Kind),tiers=Enum.GetValues<NodeKind>().Select(EcosystemHierarchy.Description).ToArray(),
        note="Положение в иерархии не даёт права управлять чужими компьютерами. Доступ определяется разрешениями и согласием владельца."
    });
    [HttpGet("ether/me")]
    [ApiCapability("ether.account.read","Прочитать игровой профиль","global")]
    public async Task<IActionResult> Me(CancellationToken ct) => Actor() is { } actor ? Ok(await ethers.Get(actor,ct)) : Forbid();
    [HttpPost("ether/register")]
    [ApiCapability("ether.account.register","Создать игровой аккаунт без персонажа","global")]
    public async Task<IActionResult> Register([FromBody] CreateEtherAccount request,CancellationToken ct)
    {
        if(Actor() is not { } actor)return Forbid();
        try{return Created("/api/v1/ecosystem/ether/me",await ethers.Register(actor,request,ct));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
}
