using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>
/// Separate server-only OAuth sign-in orchestration. State is consumed before provider exchange;
/// no client-supplied subject or account ID is accepted. No HTTP registration in this increment.
/// </summary>
public sealed class ExternalLoginService(AuthDbContext db, TimeProvider clock, IDataProtectionProvider protection,
    AuthService auth, TelegramOAuthExchange telegram, VkIdentityProof vk)
{
    private sealed record Secrets(string Verifier, string Nonce);
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private IDataProtector Protector(string hash) => protection.CreateProtector("MysticEgo.ExternalLogin.v1", hash);

    public async Task<OAuthLinkStart?> Start(ExternalCallbackConfiguration configured, string browserBinding,
        CancellationToken ct = default)
    {
        var browserHash = Hash(browserBinding);
        if (browserHash is null || !ValidConfiguration(configured)) return null;
        var now = Now;
        if (await db.ExternalLoginFlows.CountAsync(f => f.BrowserHash == browserHash && f.ExpiresUtc > now, ct) >= 3)
            return null;
        var state = RandomSecret();
        var verifier = RandomSecret();
        var nonce = RandomSecret();
        var hash = Hash(state)!;
        db.ExternalLoginFlows.Add(new ExternalLoginFlow
        {
            StateHash = hash, BrowserHash = browserHash, Provider = configured.Provider,
            ClientId = configured.ClientId, RedirectUri = configured.RedirectUri.AbsoluteUri,
            StartedUtc = now, ExpiresUtc = now.Add(Lifetime),
            ProtectedSecrets = Protector(hash).Protect(JsonSerializer.Serialize(new Secrets(verifier, nonce)))
        });
        await db.SaveChangesAsync(ct);
        return new(state, Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), nonce);
    }

    /// <summary>
    /// The caller must obtain browserBinding from a secure same-browser cookie and configuration
    /// from trusted server settings. Return session tokens only in an authenticated response body
    /// to that browser, never a redirect, query string, logs or a messenger.
    /// </summary>
    public async Task<AuthResponse?> Complete(ExternalCallbackConfiguration configured, string state,
        string browserBinding, string code, string? deviceId, string? ip, string? userAgent,
        CancellationToken ct = default)
    {
        var hash = Hash(state);
        var browserHash = Hash(browserBinding);
        if (hash is null || browserHash is null || !ValidConfiguration(configured) ||
            string.IsNullOrWhiteSpace(code) || code.Length > 4096 || code.Any(char.IsControl)) return null;
        var now = Now;
        var eligible = db.ExternalLoginFlows.Where(f => f.StateHash == hash && f.BrowserHash == browserHash &&
            f.Provider == configured.Provider && f.ExpiresUtc > now);
        var row = await eligible.AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null || await eligible.ExecuteDeleteAsync(ct) != 1) return null;
        if (row.ClientId != configured.ClientId ||
            !string.Equals(row.RedirectUri, configured.RedirectUri.AbsoluteUri, StringComparison.Ordinal)) return null;
        Secrets? secrets;
        try { secrets = JsonSerializer.Deserialize<Secrets>(Protector(hash).Unprotect(row.ProtectedSecrets)); }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return null; }
        if (secrets is null || Hash(secrets.Verifier) is null || Hash(secrets.Nonce) is null) return null;
        var claim = new OAuthProviderClaim(row.Provider, row.ClientId, configured.RedirectUri, secrets.Verifier, secrets.Nonce);
        var verified = row.Provider == "telegram"
            ? await telegram.Exchange(claim, configured.ClientSecret ?? "", code, ct)
            : await vk.Exchange(row.ClientId, claim.RedirectUri, state, state, claim.Verifier, code, deviceId ?? "", ct);
        var realm = (row.Provider == "telegram" ? "oidc:" : "oauth:") + row.ClientId;
        if (verified is null || !verified.Valid || verified.Provider != row.Provider || verified.Realm != realm ||
            row.ExpiresUtc <= Now) return null;
        // A freshly linked/relinked identity cannot authenticate a flow started before that link.
        // No account creation, merge or role changes.
        return await auth.IssueVerifiedExternalSessionAsync(verified, row.StartedUtc, ip, userAgent, ct);
    }

    public Task<int> PurgeExpired(CancellationToken ct = default) =>
        db.ExternalLoginFlows.Where(f => f.ExpiresUtc <= Now).ExecuteDeleteAsync(ct);

    private static bool ValidConfiguration(ExternalCallbackConfiguration c) =>
        c.Provider is "telegram" or "vk" && TelegramIdentityProof.PositiveId(c.ClientId) &&
        c.RedirectUri is { IsAbsoluteUri: true } && c.RedirectUri.Scheme == Uri.UriSchemeHttps &&
        c.RedirectUri.UserInfo.Length == 0 && c.RedirectUri.Fragment.Length == 0 &&
        c.RedirectUri.AbsoluteUri.Length <= 2048 &&
        (c.Provider != "telegram" || c.ClientSecret is { Length: > 0 and <= 1024 } && !c.ClientSecret.Any(char.IsControl));
    private static string RandomSecret() => Encode(RandomNumberGenerator.GetBytes(32));
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string? Hash(string? code) => code is { Length: 43 } &&
        code.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
        ? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code))) : null;
}
