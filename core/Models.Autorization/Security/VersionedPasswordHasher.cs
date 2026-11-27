using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Models.Autorization.Security;

/// <summary>
/// New passwords use salted PBKDF2-HMAC-SHA256. Reads legacy BCrypt hashes for compatibility.
/// The format includes its algorithm and cost; no password is stored or truncated.
/// </summary>
public sealed class VersionedPasswordHasher : IPasswordHasher
{
    public const int Iterations = 600_000;
    private const string Prefix = "pbkdf2-sha256";
    private static readonly UTF8Encoding Utf8 = new(false,true);

    /// <summary>Derive a 256-bit verifier using a fresh 128-bit random salt.</summary>
    public string Hash(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length > 256) throw new ArgumentException("Invalid password length");
        var salt=RandomNumberGenerator.GetBytes(16);
        var bytes=Utf8.GetBytes(password);
        try
        {
            var derived=Rfc2898DeriveBytes.Pbkdf2(bytes,salt,Iterations,HashAlgorithmName.SHA256,32);
            try { return $"{Prefix}$v1${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(derived)}"; }
            finally { CryptographicOperations.ZeroMemory(derived); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>Verify in constant time for derived keys and reject malformed or excessive costs.</summary>
    public bool Verify(string password,string hash)
    {
        if (string.IsNullOrEmpty(password) || password.Length>256 || string.IsNullOrEmpty(hash) || hash.Length>200) return false;
        try
        {
            if (hash.StartsWith("$2",StringComparison.Ordinal))
                return BCrypt.Net.BCrypt.Verify(password,hash);
            var parts=hash.Split('$');
            if(parts.Length!=5||parts[0]!=Prefix||parts[1]!="v1"||
                !int.TryParse(parts[2],NumberStyles.None,CultureInfo.InvariantCulture,out var rounds)||
                rounds<Iterations||rounds>2_000_000) return false;
            var salt=Convert.FromBase64String(parts[3]);
            var expected=Convert.FromBase64String(parts[4]);
            if(salt.Length!=16||expected.Length!=32)return false;
            var bytes=Utf8.GetBytes(password);
            try
            {
                var actual=Rfc2898DeriveBytes.Pbkdf2(bytes,salt,rounds,HashAlgorithmName.SHA256,32);
                try{return CryptographicOperations.FixedTimeEquals(actual,expected);}
                finally{CryptographicOperations.ZeroMemory(actual);}
            }
            finally{CryptographicOperations.ZeroMemory(bytes);CryptographicOperations.ZeroMemory(expected);}
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException or BCrypt.Net.SaltParseException)
        {return false;}
    }
}
