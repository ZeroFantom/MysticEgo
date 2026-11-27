using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Configuration;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;

// Minimal runnable signing/verification regression smoke test; independent of Auth HTTP server.
var dir=Path.Combine(Path.GetTempPath(),"mysticego-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
try
{
    using(var rsa=RSA.Create(3072))
    {
        File.WriteAllText(Path.Combine(dir,"private.pem"),rsa.ExportPkcs8PrivateKeyPem());
        File.WriteAllText(Path.Combine(dir,"public.pem"),rsa.ExportSubjectPublicKeyInfoPem());
    }
    var options=new JwtOptions{Issuer="test-issuer",Audience="test-audience",PrivateKeyPath=Path.Combine(dir,"private.pem"),PublicKeyPath=Path.Combine(dir,"public.pem")};
    var svc=new JwtTokenService(options);
    var uid=Guid.NewGuid();
    var sid=Guid.NewGuid();
    var (jwt,expires)=svc.GenerateAccessToken(new User{Id=uid,Login="test"},["system.developer"],["node.command"],sid);
    var verified=svc.ValidateToken(jwt);
    if(verified?.Identity?.IsAuthenticated!=true || !verified.HasClaim("permission","node.command"))throw new Exception("JWT RSA access token validation failed");
    if(verified?.FindFirst("mystic_session")?.Value!=sid.ToString("D"))throw new Exception("Missing stable session claim");
    if(expires<=DateTime.UtcNow)throw new Exception("JWT has an invalid expiry");
    var deviceSecret=Convert.ToHexString(RandomNumberGenerator.GetBytes(40));
    var hash=BCrypt.Net.BCrypt.HashPassword(deviceSecret);
    if(!BCrypt.Net.BCrypt.Verify(deviceSecret,hash) || BCrypt.Net.BCrypt.Verify("invalid",hash))throw new Exception("Device secret hashing failed");
    Console.WriteLine("AuthSmoke passed (RSA JWT, scoped permission claims and BCrypt device secrets)");
}
finally {try{Directory.Delete(dir,true);}catch{}}


// Account presentation patches must be limited to safe display values.
static bool ValidPresentation(string json)
{
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    return Api.Auth.Controllers.AccountPresentationStore.Valid(doc.RootElement);
}
if (!ValidPresentation("""{"language":"ko","font":"editorial","theme":"dark","primary":"#aabbcc","intensity":50}""") ||
    ValidPresentation("""{"language":"xx"}""") ||
    ValidPresentation("""{"font":"url(https://example.invalid)"}""") ||
    ValidPresentation("""{"primary":"red;display:none"}""") ||
    ValidPresentation("""{"allow":true}""") ||
    ValidPresentation("""{"language":"en","language":"ru"}"""))
    throw new Exception("Account presentation validation failed");
Console.WriteLine("Account presentation validation passed");


if (!Api.Auth.AccountConsentPolicy.Valid([]) || !Api.Auth.AccountConsentPolicy.Valid(["avatar-desktop"]) ||
    Api.Auth.AccountConsentPolicy.Valid(null) || Api.Auth.AccountConsentPolicy.Valid(["unknown"]) ||
    Api.Auth.AccountConsentPolicy.Valid(["avatar-desktop","avatar-desktop"]))
    throw new Exception("Feature permission policy validation failed");

var dbOptions=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:").Options;
await using(var db=new AuthDbContext(dbOptions))
{
    await db.Database.OpenConnectionAsync();
    await db.Database.EnsureCreatedAsync();
    await AccountSecuritySchema.UpgradeAsync(db,default);
    await AccountSecuritySchema.UpgradeAsync(db,default);
    var owner=new User{Login="owner",PasswordHash="test"};
    var other=new User{Login="other",PasswordHash="test"};
    db.Users.AddRange(owner,other);
    var current=Guid.NewGuid();var remote=Guid.NewGuid();var unrelated=Guid.NewGuid();
    db.RefreshTokens.AddRange(
        new RefreshToken{UserId=owner.Id,SessionId=current,Token="current",ExpiresAt=DateTime.UtcNow.AddDays(1)},
        new RefreshToken{UserId=owner.Id,SessionId=remote,Token="remote",ExpiresAt=DateTime.UtcNow.AddDays(1)},
        new RefreshToken{UserId=other.Id,SessionId=unrelated,Token="other",ExpiresAt=DateTime.UtcNow.AddDays(1)});
    await db.SaveChangesAsync();
    var count=await db.RefreshTokens.Where(x=>x.UserId==owner.Id&&(x.SessionId==null||x.SessionId!=current)).ExecuteDeleteAsync();
    if(count!=1||!await db.RefreshTokens.AnyAsync(x=>x.SessionId==current)||!await db.RefreshTokens.AnyAsync(x=>x.SessionId==unrelated))
        throw new Exception("Revoking other sessions did not preserve current/unrelated sessions");
    var grant=new AccountDeviceGrant{UserId=owner.Id,DeviceId=Guid.NewGuid(),GrantId=Guid.NewGuid(),Scope="avatar-desktop",PolicyVersion="1",GrantedUtc=DateTime.UtcNow};
    db.AccountDeviceGrants.Add(grant);await db.SaveChangesAsync();
    var revoked=await db.AccountDeviceGrants.Where(x=>x.UserId==other.Id&&x.DeviceId==grant.DeviceId).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedUtc,DateTime.UtcNow));
    if(revoked!=0)throw new Exception("Cross-account device revocation");
}
Console.WriteLine("Account security schema, scope validation and session isolation passed");
