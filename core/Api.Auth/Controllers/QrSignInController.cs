using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

[ApiController,Route("api/auth/qr"),EnableRateLimiting("auth-sensitive"),SkipPermission]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class QrSignInController(QrSignIn qr,DesktopPairing pairing,IAuthService auth,AuthDbContext db):BaseApiController
{
    public sealed record StartRequest(string Challenge);
    public sealed record InspectRequest(string Id);
    public sealed record ApproveRequest(string Id,string Comparison);
    public sealed record RedeemRequest(string Id,string Verifier);
    private Guid? Sid=>Guid.TryParse(User.FindFirst("mystic_session")?.Value,out var sid)?sid:null;
    private bool Account=>CurrentUserId.HasValue&&Sid.HasValue&&!User.HasClaim("token_use","device");

    [HttpPost("start"),AllowAnonymous,RequestSizeLimit(4096)]
    public IActionResult Start(StartRequest request)
    {
        if(!DesktopPairing.ValidSecret(request.Challenge))return BadRequest();
        var result=qr.Start(request.Challenge,Request.Headers.UserAgent.ToString(),DateTimeOffset.UtcNow);
        return result is null?StatusCode(429):Ok(result);
    }
    [HttpPost("inspect"),Authorize,RequestSizeLimit(4096)]
    public IActionResult Inspect(InspectRequest request)
    {
        if(!Account)return Unauthorized();
        var result=qr.Inspect(request.Id,DateTimeOffset.UtcNow);
        // The comparison code is only shown on the requesting device, not returned to the approver.
        return result is null?NotFound():Ok(new{result.Id,result.Expires,result.Device});
    }
    [HttpPost("approve"),Authorize,RequestSizeLimit(4096)]
    public async Task<IActionResult> Approve(ApproveRequest request,CancellationToken ct)
    {
        if(!Account)return Unauthorized();
        var uid=CurrentUserId!.Value;var sid=Sid!.Value;
        var source=await db.RefreshTokens.AsNoTracking()
            .Where(t=>t.UserId==uid&&t.SessionId==sid&&t.ExpiresAt>DateTime.UtcNow&&t.User!.IsActive)
            .Select(t=>t.Token).SingleOrDefaultAsync(ct);
        if(source is null)return Unauthorized();
        return qr.Approve(request.Id,request.Comparison,uid,source,pairing,DateTimeOffset.UtcNow)?NoContent():Conflict();
    }
    [HttpPost("redeem"),AllowAnonymous,RequestSizeLimit(4096)]
    public async Task<IActionResult> Redeem(RedeemRequest request,CancellationToken ct)
    {
        var result=qr.Redeem(request.Id,request.Verifier,DateTimeOffset.UtcNow);
        if(result.State=="pending")return Accepted();
        if(result.Grant is null)return Unauthorized();
        var login=await auth.RedeemDesktopAsync(result.Grant,request.Verifier,HttpContext.Connection.RemoteIpAddress?.ToString(),Request.Headers.UserAgent.ToString(),ct);
        return login is null?Unauthorized():Ok(login);
    }
    [HttpPost("cancel"),AllowAnonymous,RequestSizeLimit(4096)]
    public IActionResult Cancel(RedeemRequest request){qr.Cancel(request.Id,request.Verifier,DateTimeOffset.UtcNow);return NoContent();}
}
