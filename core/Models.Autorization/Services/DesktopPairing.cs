using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Models.Autorization.Services;

/// <summary>Short-lived, single-use desktop authorization grants. Never stores raw refresh tokens or browser verifier.</summary>
public sealed class DesktopPairing
{
    private sealed record Grant(Guid UserId,string SourceHash,string Challenge,DateTimeOffset Expires);
    private readonly Dictionary<string,Grant> pending=new(StringComparer.Ordinal);
    private readonly object gate=new();
    public static bool ValidSecret(string? value)=>value is not null&&Regex.IsMatch(value,@"\A[a-f0-9]{64}\z",RegexOptions.CultureInvariant);
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value))).ToLowerInvariant();
    public string? Create(Guid userId,string sourceHash,string challenge,DateTimeOffset now)
    {
        if(userId==Guid.Empty||!ValidSecret(challenge))return null;
        lock(gate)
        {
            foreach(var key in pending.Where(x=>x.Value.Expires<=now).Select(x=>x.Key).ToArray())pending.Remove(key);
            if(pending.Count>=1000||pending.Values.Count(x=>x.UserId==userId)>=5)return null;
            var code=Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
            pending.Add(Hash(code),new(userId,sourceHash,challenge,now.AddSeconds(60)));
            return code;
        }
    }
    public (Guid UserId,string SourceHash)? Consume(string? code,string? verifier,DateTimeOffset now)
    {
        if(!ValidSecret(code)||!ValidSecret(verifier))return null;
        lock(gate)
        {
            var key=Hash(code!);
            if(!pending.TryGetValue(key,out var grant))return null;
            if(grant.Expires<=now){pending.Remove(key);return null;}
            if(!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(grant.Challenge),Convert.FromHexString(Hash(verifier!))))return null;
            pending.Remove(key);
            return (grant.UserId,grant.SourceHash);
        }
    }
}
