using System.Security.Cryptography;
using System.Text;

namespace MysticEgo.Distributed;

public sealed record AgentRelease(string Version, string Sha256, long Size, string FileName, string PublishedUtc, string Signature);
public sealed record PendingAgentRelease(AgentRelease Release, string Source);

public static class AgentReleaseEndpoints
{
    public static string Payload(AgentRelease release) => $"{release.Version}\n{release.Sha256}\n{release.Size}";
    public static bool Verify(AgentRelease release, string publicPem)
    {
        if (!Version.TryParse(release.Version, out _) ||
            !System.Text.RegularExpressions.Regex.IsMatch(release.Sha256, "^[0-9a-f]{64}$") || release.Size is < 1 or > 100_000_000) return false;
        using var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(publicPem);
            return rsa.VerifyData(Encoding.UTF8.GetBytes(Payload(release)), Convert.FromBase64String(release.Signature),
                HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or CryptographicException) { return false; }
    }
}
