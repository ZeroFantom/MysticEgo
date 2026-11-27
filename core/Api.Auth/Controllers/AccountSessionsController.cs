using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;

namespace Api.Auth.Controllers;

[ApiController, Authorize, SkipPermission]
[Route("api/auth/sessions")]
public sealed class AccountSessionsController(AuthDbContext db) : BaseApiController
{
    private Guid? SessionId => Guid.TryParse(User.FindFirst("mystic_session")?.Value,out var value) ? value : null;
    private bool ValidAccount => CurrentUserId.HasValue && SessionId.HasValue && !User.HasClaim("token_use","device");

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if(!ValidAccount) return Unauthorized();
        var uid=CurrentUserId!.Value; var current=SessionId!.Value; var now=DateTime.UtcNow;
        var rows=await db.RefreshTokens.AsNoTracking()
            .Where(x=>x.UserId==uid && x.ExpiresAt>now)
            .Select(x=>new {x.SessionId,x.CreatedAt,x.LastSeenUtc,x.ExpiresAt,x.UserAgent,x.CreatedByIp})
            .ToListAsync(ct);
        Response.Headers.CacheControl="no-store";
        return Ok(rows.OrderByDescending(x=>x.LastSeenUtc??x.CreatedAt).Select(x=>new {
            id=x.SessionId, current=x.SessionId==current, createdUtc=x.CreatedAt,lastSeenUtc=x.LastSeenUtc??x.CreatedAt,
            expiresUtc=x.ExpiresAt,userAgent=x.UserAgent,ip=x.CreatedByIp,legacy=x.SessionId==null
        }));
    }

    [HttpGet("current")]
    public IActionResult Current()
    {
        if(!ValidAccount)return Unauthorized();
        Response.Headers.CacheControl="no-store";
        return Ok(new {id=SessionId,active=true});
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id,CancellationToken ct)
    {
        if(!ValidAccount)return Unauthorized();
        if(id==SessionId)return Conflict(new {code="use_logout_for_current_session"});
        var uid=CurrentUserId!.Value;
        var count=await db.RefreshTokens.Where(x=>x.UserId==uid&&x.SessionId==id).ExecuteDeleteAsync(ct);
        return count==0?NotFound():Ok(new {revoked=count});
    }

    [HttpPost("revoke-others")]
    public async Task<IActionResult> RevokeOthers(CancellationToken ct)
    {
        if(!ValidAccount)return Unauthorized();
        var uid=CurrentUserId!.Value;var sid=SessionId!.Value;
        var count=await db.RefreshTokens.Where(x=>x.UserId==uid&&(x.SessionId==null||x.SessionId!=sid)).ExecuteDeleteAsync(ct);
        return Ok(new {revoked=count});
    }
}
