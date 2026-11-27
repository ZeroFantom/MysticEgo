using System.Security.Cryptography;
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
    var (jwt,expires)=svc.GenerateAccessToken(new User{Id=uid,Login="test"},["system.developer"],["node.command"]);
    var verified=svc.ValidateToken(jwt);
    if(verified?.Identity?.IsAuthenticated!=true || !verified.HasClaim("permission","node.command"))throw new Exception("JWT RSA access token validation failed");
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
