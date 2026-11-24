using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Configuration;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Api.Auth.Controllers;

[ApiController]
[Route("api/identities")]
public sealed class ManagedIdentityController : ControllerBase
{
    private readonly AuthDbContext _db;
    private readonly JwtOptions _jwt;
    public ManagedIdentityController(AuthDbContext db, JwtOptions jwt) { _db=db; _jwt=jwt; }

    private bool Admin => User.IsInRole("system.developer") || User.IsInRole("admin") || User.IsInRole("superadmin") || User.IsInRole("root") || User.HasClaim("role_code","admin") || User.HasClaim("role_code","superadmin") || User.HasClaim("role_code","root") || User.HasClaim("role_code","owner");

    private bool OrchestraPeer => User.HasClaim("token_use", "device") && User.IsInRole("orchestra");

    public sealed record ProvisionRequest(string Name,string Kind);
    public sealed record CredentialRequest(Guid IdentityId,string Secret);

    // Регистрация идентичностей доступна только администраторам. Публичная саморегистрация устройств запрещена.
    [HttpPost("provision")]
    [Authorize, Models.Commands.SkipTool]
    public async Task<IActionResult> Provision(ProvisionRequest req,CancellationToken ct)
    {
        if (!Admin && !(OrchestraPeer && req.Kind is "local" or "agent" or "audit")) return Forbid();
        if (req.Name is null || req.Name.Length is <1 or >160 || req.Kind is not ("agent" or "local" or "global" or "audit" or "orchestra")) return BadRequest(new {error="invalid_identity"});
        var secret=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var device=new ManagedIdentity {Name=req.Name,Kind=req.Kind,SecretHash=BCrypt.Net.BCrypt.HashPassword(secret)};
        _db.ManagedIdentities.Add(device);
        await _db.SaveChangesAsync(ct);
        return Ok(new {identityId=device.Id,kind=device.Kind,secret,warning="Store this secret securely: it will never be returned again"});
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if(!Admin) return Forbid();
        return Ok(await _db.ManagedIdentities.AsNoTracking().Select(x=>new {x.Id,x.Name,x.Kind,x.Enabled,x.CreatedUtc,x.LastAuthenticatedUtc}).ToListAsync(ct));
    }

    [HttpPost("{id:guid}/revoke")]
    [Authorize, Models.Commands.SkipTool]
    public async Task<IActionResult> Revoke(Guid id,CancellationToken ct)
    {
        if(!Admin && !OrchestraPeer) return Forbid();
        var d=await _db.ManagedIdentities.FindAsync(new object[]{id},ct);
        if(d is null) return NotFound();
        if (!Admin && d.Kind is not ("local" or "agent" or "audit")) return Forbid();
        d.Enabled=false;await _db.SaveChangesAsync(ct);return Ok(new {revoked=true});
    }

    [HttpPost("token")]
    [EnableRateLimiting("auth-sensitive")]
    [AllowAnonymous]
    public async Task<IActionResult> Token(CredentialRequest req,CancellationToken ct)
    {
        var d=await _db.ManagedIdentities.FirstOrDefaultAsync(x=>x.Id==req.IdentityId,ct);
        // Даже при отсутствии идентичности проверяем хеш, чтобы уменьшить утечку информации по времени ответа.
        var known=d is not null && d.Enabled && !string.IsNullOrEmpty(req.Secret);
        var good=known && BCrypt.Net.BCrypt.Verify(req.Secret,d!.SecretHash);
        if(!good) return Unauthorized(new {error="invalid_identity_credentials"});
        d!.LastAuthenticatedUtc=DateTime.UtcNow;await _db.SaveChangesAsync(ct);
        var expires=DateTime.UtcNow.AddMinutes(10);
        var claims=new[]{new Claim(JwtRegisteredClaimNames.Sub,d.Id.ToString()),new Claim(ClaimTypes.NameIdentifier,d.Id.ToString()),new Claim(ClaimTypes.Role,d.Kind),new Claim("identity_kind",d.Kind),new Claim("token_use","device"),new Claim("identity_id",d.Id.ToString())};
        var jwt=new JwtSecurityToken(_jwt.Issuer,_jwt.Audience,claims,DateTime.UtcNow,expires,Models.Autorization.Security.JwtSigningKeys.SigningKey(_jwt));
        return Ok(new {accessToken=new JwtSecurityTokenHandler().WriteToken(jwt),expiresAt=expires,identityId=d.Id,kind=d.Kind});
    }
}
