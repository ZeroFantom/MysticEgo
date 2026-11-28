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

var qrStore=new Models.Autorization.Services.QrSignIn();
var qrPairing=new Models.Autorization.Services.DesktopPairing();
var qrVerifier=new string('a',64);var qrOther=new string('b',64);
var qrNow=DateTimeOffset.UtcNow;var qrAccount=Guid.NewGuid();
var qrView=qrStore.Start(Models.Autorization.Services.DesktopPairing.Hash(qrVerifier),"Android 15 Mobile MysticEgoAndroid/1",qrNow)??throw new Exception("QR start failed");
if(qrView.Device.Client!="MysticEgo Mobile"||qrView.Device.Os!="Android")throw new Exception("Mobile device not classified");
if(qrStore.Redeem(qrView.Id,qrOther,qrNow).State!="invalid"||qrStore.Redeem(qrView.Id,qrVerifier,qrNow).State!="pending")throw new Exception("QR proof not enforced");
if(qrStore.Approve(qrView.Id,"wrong",qrAccount,"source",qrPairing,qrNow))throw new Exception("QR comparison bypassed");
if(!qrStore.Approve(qrView.Id,qrView.Comparison,qrAccount,"source",qrPairing,qrNow)||qrStore.Approve(qrView.Id,qrView.Comparison,qrAccount,"source",qrPairing,qrNow))throw new Exception("QR approval replay");
var qrGrant=qrStore.Redeem(qrView.Id,qrVerifier,qrNow).Grant??throw new Exception("QR grant missing");
if(qrPairing.Consume(qrGrant,qrVerifier,qrNow)?.UserId!=qrAccount||qrStore.Redeem(qrView.Id,qrVerifier,qrNow).Grant!=null)throw new Exception("QR replay accepted");
var qrExpired=qrStore.Start(Models.Autorization.Services.DesktopPairing.Hash(qrVerifier),null,qrNow)!;
if(qrStore.Inspect(qrExpired.Id,qrNow.AddMinutes(3))!=null)throw new Exception("Expired QR visible");
var qrCancelled=qrStore.Start(Models.Autorization.Services.DesktopPairing.Hash(qrVerifier),null,qrNow)!;
qrStore.Cancel(qrCancelled.Id,qrOther,qrNow);
if(qrStore.Inspect(qrCancelled.Id,qrNow)==null)throw new Exception("Foreign verifier cancelled QR");
qrStore.Cancel(qrCancelled.Id,qrVerifier,qrNow);
if(qrStore.Inspect(qrCancelled.Id,qrNow)!=null)throw new Exception("QR cancellation failed");
if(Models.Autorization.Services.SessionDevice.Describe("Mozilla iPhone OS 18_0 Safari").Os!="iOS"||Models.Autorization.Services.SessionDevice.Describe(null).Kind!="unknown")throw new Exception("Device classification mismatch");
Console.WriteLine("QR challenge, comparison, approval, expiry, cancellation, replay and mobile classification passed");

var mobileModel=Models.Autorization.Services.SessionDevice.Describe("Android 15 Mobile MysticEgoAndroid/1 MysticModel/Google_Pixel_8");
if(mobileModel.Model!="Google Pixel 8"||mobileModel.Client!="MysticEgo Mobile")throw new Exception("Mobile model metadata missing");
if(Models.Autorization.Services.SessionDevice.Describe("Android 15 Mobile MysticModel/Fake").Model!=null)throw new Exception("Unmarked model accepted");

var qrLimited=qrStore.Start(Models.Autorization.Services.DesktopPairing.Hash(qrVerifier),null,qrNow)!;
for(var i=0;i<5;i++)if(qrStore.Approve(qrLimited.Id,"invalid",qrAccount,"source",qrPairing,qrNow))throw new Exception("Invalid comparison approved");
if(qrStore.Inspect(qrLimited.Id,qrNow)!=null||qrStore.Approve(qrLimited.Id,qrLimited.Comparison,qrAccount,"source",qrPairing,qrNow))throw new Exception("QR attempt limit bypassed");

var deviceQueue=new Models.Autorization.Services.DeviceActionQueue();
var deviceOwner=Guid.NewGuid();var deviceSource=Guid.NewGuid();var deviceTarget=Guid.NewGuid();var deviceNow=DateTimeOffset.UtcNow;
deviceQueue.Presence(deviceOwner,deviceTarget,true,deviceNow);
var deviceRequest=deviceQueue.Request(deviceOwner,deviceSource,deviceTarget,"open-assistant",deviceNow)??throw new Exception("Device request failed");
if(deviceQueue.Request(deviceOwner,deviceSource,deviceTarget,"shell",deviceNow)!=null||deviceQueue.List(Guid.NewGuid(),deviceTarget,deviceNow).Length!=0||deviceQueue.Decide(deviceOwner,deviceSource,deviceRequest.Id,true,deviceNow)!=null)throw new Exception("Device isolation failure");
if(deviceQueue.Decide(deviceOwner,deviceTarget,deviceRequest.Id,true,deviceNow)?.State!="approved"||deviceQueue.Decide(deviceOwner,deviceTarget,deviceRequest.Id,true,deviceNow)!=null)throw new Exception("Device replay failure");
if(deviceQueue.List(deviceOwner,deviceTarget,deviceNow.AddMinutes(3)).Length!=0)throw new Exception("Device request expiry failure");
Console.WriteLine("Device request allowlist, account isolation, target approval and replay checks passed");

var leaseQueue=new Models.Autorization.Services.DeviceActionQueue();
if(!leaseQueue.Presence(deviceOwner,deviceTarget,true,deviceNow)||!leaseQueue.Ready(deviceOwner,deviceNow).Contains(deviceTarget)||leaseQueue.Ready(Guid.NewGuid(),deviceNow).Count!=0||leaseQueue.Ready(deviceOwner,deviceNow.AddSeconds(36)).Count!=0)throw new Exception("Receiving lease isolation or expiry failure");
leaseQueue.Presence(deviceOwner,deviceTarget,true,deviceNow);leaseQueue.Presence(deviceOwner,deviceTarget,false,deviceNow);if(leaseQueue.Ready(deviceOwner,deviceNow).Count!=0)throw new Exception("Receive disable failed");
leaseQueue.Presence(deviceOwner,deviceTarget,true,deviceNow);
var cancelRequest=leaseQueue.Request(deviceOwner,deviceSource,deviceTarget,"open-profile",deviceNow)!;
if(leaseQueue.Cancel(deviceOwner,deviceTarget,cancelRequest.Id,deviceNow)!=null||leaseQueue.Cancel(deviceOwner,deviceSource,cancelRequest.Id,deviceNow)?.State!="cancelled"||leaseQueue.Decide(deviceOwner,deviceTarget,cancelRequest.Id,true,deviceNow)!=null)throw new Exception("Request cancellation isolation failure");
Console.WriteLine("Receiving lease expiry and source cancellation passed");

var capabilityQueue=new Models.Autorization.Services.DeviceActionQueue();
if(capabilityQueue.Request(deviceOwner,deviceSource,deviceTarget,"signal-device",deviceNow)!=null)throw new Exception("Offline request accepted");
if(capabilityQueue.Presence(deviceOwner,deviceTarget,true,deviceNow,["shell"]))throw new Exception("Unknown capability registered");
capabilityQueue.Presence(deviceOwner,deviceTarget,true,deviceNow,["signal-device","open-app-settings"]);
if(capabilityQueue.Request(deviceOwner,deviceSource,deviceTarget,"show-device-info",deviceNow)!=null)throw new Exception("Unadvertised capability accepted");
var nativeRequest=capabilityQueue.Request(deviceOwner,deviceSource,deviceTarget,"open-app-settings",deviceNow)!;
capabilityQueue.Presence(deviceOwner,deviceTarget,true,deviceNow,["signal-device"]);
if(capabilityQueue.Decide(deviceOwner,deviceTarget,nativeRequest.Id,true,deviceNow)!=null||capabilityQueue.List(deviceOwner,deviceSource,deviceNow).Single(i=>i.Id==nativeRequest.Id).State!="cancelled")throw new Exception("Removed capability request survived");
var signalRequest=capabilityQueue.Request(deviceOwner,deviceSource,deviceTarget,"signal-device",deviceNow)!;
if(capabilityQueue.Decide(deviceOwner,deviceTarget,signalRequest.Id,true,deviceNow.AddSeconds(36))!=null)throw new Exception("Expired receiving lease approved");
capabilityQueue.Presence(deviceOwner,deviceTarget,true,deviceNow.AddSeconds(37),["signal-device"]);
if(capabilityQueue.Decide(deviceOwner,deviceTarget,signalRequest.Id,true,deviceNow.AddSeconds(37))!=null)throw new Exception("Expired request resurrected");
Console.WriteLine("Device capability, atomic receiving lease and cancellation tests passed");
