using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Configuration;
namespace Models.Autorization.Security;
/// <summary>Auth holds RSA private key, other services only distribute the public key.</summary>
public static class JwtSigningKeys
{
    public static SecurityKey VerificationKey(JwtOptions opts)
    {
        if(!string.IsNullOrWhiteSpace(opts.PublicKeyPath))
        {
            using var rsa=RSA.Create();rsa.ImportFromPem(File.ReadAllText(opts.PublicKeyPath));
            var parameters=rsa.ExportParameters(false);
            var pub=RSA.Create();pub.ImportParameters(parameters);
            return new RsaSecurityKey(pub);
        }
        if(opts.SecretKey.Length>=32)return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SecretKey));
        throw new InvalidOperationException("Требуется открытый ключ JWT или HMAC-ключ длиной от 32 символов");
    }
    public static SigningCredentials SigningKey(JwtOptions opts)
    {
        if(!string.IsNullOrWhiteSpace(opts.PrivateKeyPath))
        {
            var rsa=RSA.Create();rsa.ImportFromPem(File.ReadAllText(opts.PrivateKeyPath));
            return new SigningCredentials(new RsaSecurityKey(rsa),SecurityAlgorithms.RsaSha256);
        }
        if(opts.SecretKey.Length>=32)return new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.SecretKey)),SecurityAlgorithms.HmacSha256);
        throw new InvalidOperationException("Для API авторизации требуется закрытый ключ подписи JWT");
    }
}
