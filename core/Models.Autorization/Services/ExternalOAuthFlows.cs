using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

public sealed class OAuthLinkStart(string state, string challenge, string nonce)
{
    public string State { get; } = state;
    public string Challenge { get; } = challenge;
    public string Nonce { get; } = nonce;
}

/// <summary>Server-only data from a consumed callback. Never serialize to a client or log.</summary>
public class OAuthProviderClaim(string provider, string clientId, Uri redirectUri, string verifier, string nonce)
{
    public string Provider { get; } = provider;
    public string ClientId { get; } = clientId;
    public Uri RedirectUri { get; } = redirectUri;
    public string Verifier { get; } = verifier;
    public string Nonce { get; } = nonce;
}

public sealed class OAuthLinkClaim(string provider, string clientId, Uri redirectUri, string verifier,
    string nonce, string linkCode) : OAuthProviderClaim(provider, clientId, redirectUri, verifier, nonce)
{
    internal string LinkCode { get; } = linkCode;
}

/// <summary>
/// One-use OAuth state for account linking only. Caller supplies trusted configuration, the
/// authenticated account/session and a high-entropy browser binding held outside the URL.
/// No endpoint, automatic login, or provider-proof bypass. Data protection keys must be
/// shared and protected across production Auth instances; no plaintext verifier in the DB.
/// </summary>
public sealed class ExternalOAuthFlows(AuthDbContext db, TimeProvider clock, IDataProtectionProvider protection)
{
    private sealed record Secrets(string Verifier, string Nonce, string LinkCode);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private IDataProtector Protector(string hash) =>
        protection.CreateProtector("MysticEgo.ExternalOAuthLink.v1", hash);
    private IQueryable<ExternalLinkIntent> LiveLinks(DateTime now) =>
        db.ExternalLinkIntents.Where(i => i.ExpiresUtc > now && i.VerifiedSubject == null &&
            db.RefreshTokens.Any(t => t.UserId == i.UserId && t.SessionId == i.SessionId &&
                t.ExpiresAt > now && t.User!.IsActive));

    /// <summary>Starts a new flow; account/session must be verified by the caller.</summary>
    public async Task<OAuthLinkStart?> Start(Guid account, Guid session, string linkCode, string browserBinding,
        string provider, string clientId, Uri redirectUri, CancellationToken ct = default)
    {
        var linkHash = Hash(linkCode);
        var browserHash = Hash(browserBinding);
        if (account == Guid.Empty || session == Guid.Empty || linkHash is null || browserHash is null ||
            linkHash == browserHash || !TelegramIdentityProof.PositiveId(clientId) ||
            !ValidRedirect(redirectUri) || provider is not ("telegram" or "vk")) return null;
        var realm = (provider == "telegram" ? "oidc:" : "oauth:") + clientId;
        var now = Now;
        var link = await LiveLinks(now).AsNoTracking().SingleOrDefaultAsync(i =>
            i.CodeHash == linkHash && i.UserId == account && i.SessionId == session &&
            i.Provider == provider && i.Realm == realm, ct);
        if (link is null || await db.ExternalOAuthFlows.AnyAsync(f => f.LinkHash == linkHash, ct)) return null;
        var state = RandomSecret();
        var verifier = RandomSecret();
        var nonce = RandomSecret();
        var hash = Hash(state)!;
        db.ExternalOAuthFlows.Add(new ExternalOAuthFlow
        {
            StateHash = hash, BrowserHash = browserHash, LinkHash = linkHash, Provider = provider,
            ClientId = clientId, RedirectUri = redirectUri.AbsoluteUri, ExpiresUtc = link.ExpiresUtc,
            ProtectedSecrets = Protector(hash).Protect(JsonSerializer.Serialize(new Secrets(verifier, nonce, linkCode)))
        });
        // Unique LinkHash also rejects concurrent starts. Failed units of work must be discarded.
        await db.SaveChangesAsync(ct);
        return new(state, Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))), nonce);
    }

    /// <summary>
    /// Claim BEFORE exchanging the provider code. Conditional deletion elects a single callback
    /// even across processes. A failed exchange requires a fresh flow; never replay a claim.
    /// </summary>
    public async Task<OAuthLinkClaim?> Claim(string state, string browserBinding, string provider,
        CancellationToken ct = default)
    {
        var hash = Hash(state);
        var browserHash = Hash(browserBinding);
        if (hash is null || browserHash is null || provider is not ("telegram" or "vk")) return null;
        var now = Now;
        var eligible = db.ExternalOAuthFlows.Where(f => f.StateHash == hash && f.BrowserHash == browserHash &&
            f.Provider == provider && f.ExpiresUtc > now &&
            LiveLinks(now).Any(i => i.CodeHash == f.LinkHash));
        var row = await eligible.AsNoTracking().SingleOrDefaultAsync(ct);
        if (row is null) return null;
        // No provider calls before the one-use state is consumed.
        if (await eligible.ExecuteDeleteAsync(ct) != 1) return null;
        try
        {
            var secrets = JsonSerializer.Deserialize<Secrets>(Protector(hash).Unprotect(row.ProtectedSecrets));
            if (secrets is null || Hash(secrets.LinkCode) != row.LinkHash ||
                Hash(secrets.Verifier) is null || Hash(secrets.Nonce) is null ||
                !Uri.TryCreate(row.RedirectUri, UriKind.Absolute, out var redirect) || !ValidRedirect(redirect))
                return null;
            return new(row.Provider, row.ClientId, redirect, secrets.Verifier, secrets.Nonce, secrets.LinkCode);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or FormatException) { return null; }
    }

    /// <summary>
    /// Only call after the trusted provider adapter validated proof using this exact claim.
    /// Stages a candidate, not a link or session. Existing explicit confirmation remains mandatory.
    /// </summary>
    public Task<bool> StageVerifiedProof(OAuthLinkClaim claim, ExternalIdentityKey proof, CancellationToken ct = default)
    {
        var realm = (claim.Provider == "telegram" ? "oidc:" : "oauth:") + claim.ClientId;
        if (!proof.Valid || proof.Provider != claim.Provider || proof.Realm != realm) return Task.FromResult(false);
        return new ExternalLinkIntents(db, clock).ObserveVerifiedSubject(claim.LinkCode, proof, ct);
    }

    public Task<int> PurgeExpired(CancellationToken ct = default) =>
        db.ExternalOAuthFlows.Where(f => f.ExpiresUtc <= Now).ExecuteDeleteAsync(ct);

    private static bool ValidRedirect(Uri? uri) => uri is { IsAbsoluteUri: true } &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsoluteUri.Length <= 2048;
    private static string RandomSecret() => Encode(RandomNumberGenerator.GetBytes(32));
    private static string Encode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+','-').Replace('/','_');
    private static string? Hash(string? code) => code is { Length: 43 } &&
        code.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
        ? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code))) : null;
}
