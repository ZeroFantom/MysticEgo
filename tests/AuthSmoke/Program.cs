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
