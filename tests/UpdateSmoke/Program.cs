using MysticEgo.Distributed;
using System.Security.Cryptography;
using System.Text;

// Независимая проверка RSA-PSS и SHA-256 для манифеста обновления агента.
using var rsa=RSA.Create(3072);
var sha=new string('a',64);
var version="0.5.1";
var body=$"{version}\n{sha}\n{12345L}";
var signature=Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(body),HashAlgorithmName.SHA256,RSASignaturePadding.Pss));
var manifest=new AgentRelease(version,sha,12345,"agent.zip",DateTimeOffset.UtcNow.ToString("O"),signature);
var pub=rsa.ExportSubjectPublicKeyInfoPem();
if(!AgentReleaseEndpoints.Verify(manifest,pub))throw new Exception("Подпись должна проходить проверку");
if(AgentReleaseEndpoints.Verify(manifest with {Version="0.5.2"},pub))throw new Exception("Подмена версии не должна проходить проверку");
if(AgentReleaseEndpoints.Verify(manifest with {Sha256=new string('b',64)},pub))throw new Exception("Подмена хеша не должна проходить проверку");
if(AgentReleaseEndpoints.Verify(manifest with {Size=54321},pub))throw new Exception("Подмена размера не должна проходить проверку");
using var other=RSA.Create(3072);
if(AgentReleaseEndpoints.Verify(manifest,other.ExportSubjectPublicKeyInfoPem()))throw new Exception("Чужой ключ не должен проходить проверку");
Console.WriteLine("UpdateSmoke passed");
