using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>
/// Server-side linking primitives only. Start and Confirm require an authenticated account,
/// its actual current session and recent reauthentication enforced by the calling application.
/// ObserveVerifiedSubject is ONLY for a trusted provider adapter after cryptographic proof
/// validation. No methods are exposed through HTTP, MCP or DI in this increment.
/// Each operation uses a dedicated DbContext/unit of work; do not reuse it after a failed write.
/// </summary>
public sealed class ExternalLinkIntents(AuthDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private IQueryable<RefreshToken> ActiveSessions(DateTime now) => db.RefreshTokens
        .Where(t => t.SessionId != null && t.ExpiresAt > now && t.User!.IsActive);

    private IQueryable<ExternalLinkIntent> Live(string hash, DateTime now) =>
        db.ExternalLinkIntents.Where(i => i.CodeHash == hash && i.ExpiresUtc > now &&
            ActiveSessions(now).Any(t => t.UserId == i.UserId && t.SessionId == i.SessionId));

    /// <summary>Generate a 256-bit URL-safe challenge; caller must keep it out of logs.</summary>
    public async Task<string?> Start(Guid account, Guid session, string provider, string realm,
        CancellationToken ct = default)
    {
        var now = Now;
        if (account == Guid.Empty || session == Guid.Empty ||
            !new ExternalIdentityKey(provider, realm, "placeholder").Valid ||
            !await ActiveSessions(now).AnyAsync(t => t.UserId == account && t.SessionId == session, ct))
            return null;
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        db.ExternalLinkIntents.Add(new ExternalLinkIntent
        {
            CodeHash = Hash(code)!, UserId = account, SessionId = session, Provider = provider,
            Realm = realm, ExpiresUtc = now.Add(Lifetime)
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    /// <summary>
    /// Bind the first verified provider subject to this live intent. This does not create a link.
    /// Subsequent observations cannot overwrite the candidate, even from the same provider.
    /// </summary>
    public async Task<bool> ObserveVerifiedSubject(string code, ExternalIdentityKey verified, CancellationToken ct = default)
    {
        var hash = Hash(code);
        if (hash is null || !verified.Valid) return false;
        return await Live(hash, Now)
            .Where(i => i.Provider == verified.Provider && i.Realm == verified.Realm && i.VerifiedSubject == null)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.VerifiedSubject, verified.Subject), ct) == 1;
    }

    /// <summary>Only the originating account/session can read the exact verified candidate for confirmation.</summary>
    public async Task<ExternalIdentityKey?> Candidate(Guid account, Guid session, string code, CancellationToken ct = default)
    {
        var hash = Hash(code);
        if (hash is null || account == Guid.Empty || session == Guid.Empty) return null;
        var row = await Live(hash, Now).AsNoTracking()
            .SingleOrDefaultAsync(i => i.UserId == account && i.SessionId == session && i.VerifiedSubject != null, ct);
        return row is null ? null : new(row.Provider, row.Realm, row.VerifiedSubject!);
    }

    /// <summary>
    /// Explicit confirmation of the displayed candidate in the original session.
    /// Consume and persist in one serializable transaction. A failed/conflicting transaction
    /// must be reported as unavailable by the caller, never retried as another account.
    /// </summary>
    public async Task<ExternalIdentityLinkResult> Confirm(Guid account, Guid session, string code,
        ExternalIdentityKey displayed, CancellationToken ct = default)
    {
        var hash = Hash(code);
        if (hash is null || !displayed.Valid || account == Guid.Empty || session == Guid.Empty)
            return ExternalIdentityLinkResult.Unavailable;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var claimed = await Live(hash, Now)
            .Where(i => i.UserId == account && i.SessionId == session && i.Provider == displayed.Provider &&
                i.Realm == displayed.Realm && i.VerifiedSubject == displayed.Subject)
            .ExecuteDeleteAsync(ct);
        if (claimed != 1) return ExternalIdentityLinkResult.Unavailable;
        var result = await new ExternalIdentityStore(db).RecordVerifiedLink(account, displayed, ct);
        if (result == ExternalIdentityLinkResult.Linked)
        {
            db.ExternalIdentityAudits.Add(new ExternalIdentityAudit
            {
                UserId = account, SessionId = session, Provider = displayed.Provider, Realm = displayed.Realm,
                Action = "linked", OccurredUtc = Now
            });
            await db.SaveChangesAsync(ct);
        }
        // An ordinary ownership conflict consumes the intent without linking. A database failure
        // rolls back the transaction, so neither consumption nor association is committed.
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>Trusted maintenance operation; production must schedule and bound cleanup.</summary>
    public Task<int> PurgeExpired(CancellationToken ct = default) =>
        db.ExternalLinkIntents.Where(i => i.ExpiresUtc <= Now).ExecuteDeleteAsync(ct);

    private static string? Hash(string? code) =>
        code is { Length: 43 } && code.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
            ? Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(code))) : null;
}
