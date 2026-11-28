using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Only a pinned managed Orchestra identity can resolve a Mini App capability.
/// This route must not be published through public proxies. No arbitrary account lookup.</summary>
[ApiController, Authorize, SkipPermission, Route("api/auth/messenger/internal/mini")]
[EnableRateLimiting("messenger-webhook")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TelegramMiniInternalController(AuthDbContext db,TelegramMiniSessions sessions,
    IConfiguration configuration):ControllerBase
{
    [HttpPost("resolve"),Models.Commands.SkipTool,RequestSizeLimit(1024)]
    public async Task<IActionResult> Resolve(CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        if(!configuration.GetValue<bool>("Messenger:Telegram:MiniApp:Enabled")||
            !configuration.GetValue<bool>("Messenger:Telegram:MiniApp:SoulReadEnabled"))return NotFound();
        if(!Guid.TryParse(configuration["Messenger:Telegram:MiniApp:OrchestraIdentityId"],out var configured)||
            configured==Guid.Empty||User.Identity?.IsAuthenticated!=true||!User.HasClaim("token_use","device")||
            !User.HasClaim("identity_kind","orchestra")||!User.IsInRole("orchestra")||
            !Guid.TryParse(User.FindFirst("identity_id")?.Value,out var id)||id!=configured||
            !await db.ManagedIdentities.AsNoTracking().AnyAsync(x=>x.Id==id&&x.Enabled&&x.Kind=="orchestra",ct))
            return StatusCode(403);
        var bot=MessengerConfiguration.Read(configuration,"telegram");
        if(bot is null||!TelegramMiniProof.ValidToken(bot.Value.Realm,configuration["Messenger:Telegram:AccessToken"]))
            return NotFound();
        var header=Request.Headers["X-Mystic-Mini-Session"];
        var principal=header.Count==1?await sessions.Resolve(bot.Value.Realm,header.ToString(),ct):null;
        return principal is null?Unauthorized():Ok(new{userId=principal.UserId});
    }
}
