using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models.Autorization.Attributes;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Dedicated Mini App API. These tokens are not accepted as Auth bearer tokens.
/// No identity/character IDs, privileged operations or group content are returned.</summary>
[ApiController, AllowAnonymous, SkipPermission, Route("api/auth/messenger/telegram/mini")]
[EnableRateLimiting("external-link")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class TelegramMiniController(IConfiguration configuration,TelegramMiniSessions sessions,TimeProvider clock):ControllerBase
{
    public sealed record ExchangeRequest(string InitData);
    private (MessengerRealm Realm,string Token)? Settings()
    {
        if(!configuration.GetValue<bool>("Messenger:Telegram:MiniApp:Enabled"))return null;
        var bot=MessengerConfiguration.Read(configuration,"telegram");
        var token=configuration["Messenger:Telegram:AccessToken"];
        if(bot is null||!TelegramMiniProof.ValidToken(bot.Value.Realm,token))return null;
        return (bot.Value.Realm,token!);
    }
    private bool OriginAllowed()
    {
        return Uri.TryCreate(configuration["Messenger:Telegram:MiniApp:Origin"],UriKind.Absolute,out var uri)&&
            uri.Scheme=="https"&&uri.UserInfo.Length==0&&uri.AbsolutePath=="/"&&uri.Query.Length==0&&uri.Fragment.Length==0&&
            Request.Headers.Origin.Count==1&&Request.Headers.Origin.ToString()==uri.GetLeftPart(UriPartial.Authority);
    }
    private string? Token()
    {
        var header=Request.Headers["X-Mystic-Mini-Session"];
        return header.Count==1?header.ToString():null;
    }
    private void PrivateResponse()
    {
        Response.Headers.CacheControl="no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
        Response.Headers["X-Content-Type-Options"]="nosniff";
    }
    [HttpPost("exchange"),Models.Commands.SkipTool,RequestSizeLimit(32768)]
    public async Task<IActionResult> Exchange(ExchangeRequest input,CancellationToken ct)
    {
        PrivateResponse();
        var settings=Settings();if(settings is null)return NotFound();
        if(!OriginAllowed())return StatusCode(403);
        var proof=TelegramMiniProof.Verify(settings.Value.Realm,settings.Value.Token,input.InitData,clock.GetUtcNow());
        if(proof is null)return Unauthorized();
        var session=await sessions.Exchange(proof,ct);
        return session is null?Unauthorized():Ok(new{sessionToken=session.Token,expiresInSeconds=session.ExpiresInSeconds});
    }
    [HttpPost("status"),Models.Commands.SkipTool,RequestSizeLimit(1024)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        PrivateResponse();
        var settings=Settings();if(settings is null)return NotFound();
        if(!OriginAllowed())return StatusCode(403);
        var principal=await sessions.Resolve(settings.Value.Realm,Token(),ct);
        return principal is null?Unauthorized():Ok(new{linked=true,scope="mini-app",worldAvailable=false});
    }
    [HttpPost("logout"),Models.Commands.SkipTool,RequestSizeLimit(1024)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        PrivateResponse();
        var settings=Settings();if(settings is null)return NotFound();
        if(!OriginAllowed())return StatusCode(403);
        await sessions.Revoke(settings.Value.Realm,Token(),ct);
        return NoContent();
    }
}
