using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Api.Auth
{
    public static class AccountConsentPolicy
    {
        public const string Version="1";
        public static readonly string[] Scopes=["avatar-desktop","browser-control","desktop-control","screen-capture","recording","replay","peripherals","vendor-updates","game-control"];
        public static bool Valid(string[]? scopes)=>scopes is not null && scopes.Length<=Scopes.Length &&
            scopes.Distinct(StringComparer.Ordinal).Count()==scopes.Length && scopes.All(s=>Scopes.Contains(s,StringComparer.Ordinal));
    }
}

namespace Api.Auth.Controllers
{
    [ApiController, Authorize, SkipPermission]
    [Route("api/auth")]
    public sealed class AccountConsentController(AuthDbContext db,IConfiguration config) : BaseApiController
    {
        private Guid? Owner => User.HasClaim("token_use","device") || !Guid.TryParse(User.FindFirst("mystic_session")?.Value,out _) ? null : CurrentUserId;
        public sealed record Review(string TermsVersion,string PrivacyVersion,string LicenseVersion,string PermissionPolicyVersion,string[] EnabledScopes);
        public sealed record DeviceGrant(Guid DeviceId,Guid GrantId,string Scope,string PolicyVersion);
        public sealed record RevokeGrant(Guid DeviceId,string Scope);

        [HttpGet("consents")]
        public async Task<IActionResult> Get(CancellationToken ct)
        {
            if(Owner is not Guid uid)return Unauthorized();
            var docs=AuditRegistration.ReadDocuments(config);
            if(docs is null)return StatusCode(503);
            var acceptance=await db.AuditRegistrationAcceptances.AsNoTracking().SingleOrDefaultAsync(x=>x.UserId==uid,ct);
            var license=await db.AccountLicenseAcceptances.AsNoTracking().AnyAsync(x=>x.UserId==uid&&x.Version==docs.License.Version,ct);
            var choices=await db.AccountFeatureConsents.AsNoTracking().Where(x=>x.UserId==uid&&x.PolicyVersion==AccountConsentPolicy.Version).ToListAsync(ct);
            var grants=await db.AccountDeviceGrants.AsNoTracking().Where(x=>x.UserId==uid&&x.RevokedUtc==null).ToListAsync(ct);
            Response.Headers.CacheControl="no-store";
            return Ok(new {terms=docs.Terms,privacy=docs.Privacy,license=docs.License,permissionPolicyVersion=AccountConsentPolicy.Version,
                scopes=AccountConsentPolicy.Scopes,enabledScopes=choices.Where(x=>x.Enabled).Select(x=>x.Scope),
                accepted=license&&acceptance?.TermsVersion==docs.Terms.Version&&acceptance.PrivacyVersion==docs.Privacy.Version&&choices.Count==AccountConsentPolicy.Scopes.Length,
                grants=grants.Select(x=>new{x.DeviceId,x.GrantId,x.Scope,x.PolicyVersion,x.GrantedUtc})});
        }

        [HttpPost("consents"),RequestSizeLimit(8192)]
        public async Task<IActionResult> ReviewConsents(Review input,CancellationToken ct)
        {
            if(Owner is not Guid uid)return Unauthorized();
            var docs=AuditRegistration.ReadDocuments(config);
            if(docs is null)return StatusCode(503);
            if(input.TermsVersion!=docs.Terms.Version||input.PrivacyVersion!=docs.Privacy.Version||input.LicenseVersion!=docs.License.Version||
                input.PermissionPolicyVersion!=AccountConsentPolicy.Version||!AccountConsentPolicy.Valid(input.EnabledScopes))return BadRequest(new{code="invalid_consent"});
            var now=DateTime.UtcNow;
            var acceptance=await db.AuditRegistrationAcceptances.SingleOrDefaultAsync(x=>x.UserId==uid,ct);
            if(acceptance is null){acceptance=new(){UserId=uid,TermsVersion=docs.Terms.Version,PrivacyVersion=docs.Privacy.Version,AcceptedUtc=now};db.Add(acceptance);}
            else if(acceptance.TermsVersion!=docs.Terms.Version||acceptance.PrivacyVersion!=docs.Privacy.Version)
            {acceptance.TermsVersion=docs.Terms.Version;acceptance.PrivacyVersion=docs.Privacy.Version;acceptance.AcceptedUtc=now;}
            if(!await db.AccountLicenseAcceptances.AnyAsync(x=>x.UserId==uid&&x.Version==docs.License.Version,ct))
                db.AccountLicenseAcceptances.Add(new(){UserId=uid,Version=docs.License.Version,AcceptedUtc=now});
            var choices=await db.AccountFeatureConsents.Where(x=>x.UserId==uid).ToListAsync(ct);
            foreach(var scope in AccountConsentPolicy.Scopes)
            {
                var enabled=input.EnabledScopes.Contains(scope,StringComparer.Ordinal);
                var row=choices.SingleOrDefault(x=>x.Scope==scope);
                if(row is null)db.AccountFeatureConsents.Add(new(){UserId=uid,Scope=scope,Enabled=enabled,PolicyVersion=AccountConsentPolicy.Version,UpdatedUtc=now});
                else{row.Enabled=enabled;row.PolicyVersion=AccountConsentPolicy.Version;row.UpdatedUtc=now;}
            }
            var grants=await db.AccountDeviceGrants.Where(x=>x.UserId==uid&&x.RevokedUtc==null).ToListAsync(ct);
            foreach(var grant in grants)if(!input.EnabledScopes.Contains(grant.Scope,StringComparer.Ordinal)||grant.PolicyVersion!=AccountConsentPolicy.Version)grant.RevokedUtc=now;
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        // Server record alone is not proof of OS consent. Native clients must also possess
        // the locally confirmed matching grant generation before enabling the operation.
        [HttpPost("device-grants/approve"),RequestSizeLimit(4096)]
        public async Task<IActionResult> Approve(DeviceGrant input,CancellationToken ct)
        {
            if(Owner is not Guid uid)return Unauthorized();
            if(input.DeviceId==Guid.Empty||input.GrantId==Guid.Empty||input.PolicyVersion!=AccountConsentPolicy.Version||
                !AccountConsentPolicy.Scopes.Contains(input.Scope,StringComparer.Ordinal))return BadRequest();
            var docs=AuditRegistration.ReadDocuments(config);
            if(docs is null)return StatusCode(503);
            if(!await db.AuditRegistrationAcceptances.AnyAsync(x=>x.UserId==uid&&x.TermsVersion==docs.Terms.Version&&x.PrivacyVersion==docs.Privacy.Version,ct)||
                !await db.AccountLicenseAcceptances.AnyAsync(x=>x.UserId==uid&&x.Version==docs.License.Version,ct)||
                !await db.AccountFeatureConsents.AnyAsync(x=>x.UserId==uid&&x.Scope==input.Scope&&x.Enabled&&x.PolicyVersion==AccountConsentPolicy.Version,ct))return Forbid();
            var row=await db.AccountDeviceGrants.SingleOrDefaultAsync(x=>x.UserId==uid&&x.DeviceId==input.DeviceId&&x.Scope==input.Scope,ct);
            if(row is null)db.AccountDeviceGrants.Add(new(){UserId=uid,DeviceId=input.DeviceId,GrantId=input.GrantId,Scope=input.Scope,PolicyVersion=input.PolicyVersion,GrantedUtc=DateTime.UtcNow});
            else{row.GrantId=input.GrantId;row.PolicyVersion=input.PolicyVersion;row.GrantedUtc=DateTime.UtcNow;row.RevokedUtc=null;}
            await db.SaveChangesAsync(ct);
            return NoContent();
        }

        [HttpPost("device-grants/revoke"),RequestSizeLimit(4096)]
        public async Task<IActionResult> Revoke(RevokeGrant input,CancellationToken ct)
        {
            if(Owner is not Guid uid)return Unauthorized();
            await db.AccountDeviceGrants.Where(x=>x.UserId==uid&&x.DeviceId==input.DeviceId&&x.Scope==input.Scope&&x.RevokedUtc==null)
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedUtc,DateTime.UtcNow),ct);
            return NoContent();
        }
    }
}
