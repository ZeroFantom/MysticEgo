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


// Verify the shared credential policy, Unicode lengths and stored hash format.
var policy=new PasswordPolicy();
if(!CredentialPolicy.ValidLogin("zero.fantom")||CredentialPolicy.ValidLogin("a")||
   CredentialPolicy.ValidLogin("a bcd")||CredentialPolicy.ValidLogin("abcd\n")||
   CredentialPolicy.ValidLogin("-abcd")||CredentialPolicy.ValidLogin(new string('a',41)))
    throw new Exception("Login policy regression");
if(CredentialPolicy.PasswordError("correct horse battery staple","owner",policy)!=null||
   CredentialPolicy.PasswordError("123456789012345","owner",policy)==null||
   CredentialPolicy.PasswordError("too short","owner",policy)==null||
   CredentialPolicy.PasswordError("long password\nwith newline","owner",policy)==null||
   CredentialPolicy.PasswordError(new string('界',129),"owner",policy)==null)
    throw new Exception("Password policy regression");
var hasher=new VersionedPasswordHasher();
var password=string.Concat(Enumerable.Repeat("한글密碼",25))+" A";
var first=hasher.Hash(password);var second=hasher.Hash(password);
if(first==second||first.Length>200||!hasher.Verify(password,first)||!hasher.Verify(password,second)||
   hasher.Verify(password+"x",first)||hasher.Verify(password,first.Replace("$600000$","$999999999$"))||
   hasher.Verify(password,"pbkdf2-sha256$v1$600000$invalid$invalid"))
    throw new Exception("Versioned hash regression");
var prefix=new string('x',72);
if(hasher.Verify(prefix+"different",hasher.Hash(prefix+"original")))
    throw new Exception("Password suffix after 72 bytes was ignored");
var legacy=BCrypt.Net.BCrypt.HashPassword("legacy test password");
if(!hasher.Verify("legacy test password",legacy)||hasher.Verify("wrong",legacy))
    throw new Exception("Legacy BCrypt compatibility regression");
Console.WriteLine("Credential policy, Unicode and versioned hashing smoke checks passed");

var grants=new Models.Autorization.Services.DesktopPairing();
var instant=DateTimeOffset.UtcNow;
var verifier=new string('b',64);
var challenge=Models.Autorization.Services.DesktopPairing.Hash(verifier);
var account=Guid.NewGuid();
var code=grants.Create(account,"source-proof",challenge,instant)??throw new Exception("Pairing grant missing");
if(grants.Consume(code,new string('c',64),instant)!=null)throw new Exception("Wrong verifier accepted");
var accepted=grants.Consume(code,verifier,instant);
if(accepted?.UserId!=account||accepted?.SourceHash!="source-proof")throw new Exception("Pairing grant not bound to source");
if(grants.Consume(code,verifier,instant)!=null)throw new Exception("Pairing replay accepted");
code=grants.Create(account,"source-proof",challenge,instant)!;
if(grants.Consume(code,verifier,instant.AddSeconds(61))!=null)throw new Exception("Expired pairing accepted");
if(grants.Create(account,"source-proof","bad",instant)!=null)throw new Exception("Invalid challenge accepted");
Console.WriteLine("Desktop pairing proof, expiry and replay tests passed");

await ExternalIdentitySmoke.Run();

await ExternalLinkSmoke.Run();

await ProviderProofSmoke.Run();

await ExternalOAuthSmoke.Run();

await OAuthCallbackSmoke.Run();

await ExternalAccountApiSmoke.Run();

await ExternalRevocationSmoke.Run();

await ExternalLoginSmoke.Run();

ExternalLoginHttpSmoke.Run();

await MessengerIdentitySmoke.Run();

await MessengerIngressSmoke.Run();

await MessengerOutboundSmoke.Run();

await MessengerWorldHandoffSmoke.Run();
