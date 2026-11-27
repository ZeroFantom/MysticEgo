using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Proof of an existing user refresh session plus a browser-generated challenge. Device JWTs cannot mint user sessions.</summary>
[ApiController,Route("api/auth/desktop"),EnableRateLimiting("auth-sensitive")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DesktopPairingController(IAuthService auth):ControllerBase
{
    public sealed record ApproveRequest(string RefreshToken,string Challenge);
    public sealed record RedeemRequest(string Code,string Verifier);
    [HttpPost("approve"),AllowAnonymous,RequestSizeLimit(8192)]
    public async Task<IActionResult> Approve(ApproveRequest req,CancellationToken ct)
    {
        var code=await auth.ApproveDesktopAsync(req.RefreshToken,req.Challenge,ct);
        return code is null?Unauthorized():Ok(new{code});
    }
    [HttpPost("redeem"),AllowAnonymous,RequestSizeLimit(4096)]
    public async Task<IActionResult> Redeem(RedeemRequest req,CancellationToken ct)
    {
        var result=await auth.RedeemDesktopAsync(req.Code,req.Verifier,HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),ct);
        return result is null?Unauthorized():Ok(result);
    }
}
