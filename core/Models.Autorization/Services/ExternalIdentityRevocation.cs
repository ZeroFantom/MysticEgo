using System.Data;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;

namespace Models.Autorization.Services;

public sealed record ExternalUnlinkResult(int RevokedSessions);

/// <summary>
/// Removes an exact owned external identity and revokes every account refresh session in one
/// serializable transaction. Password reauthentication is mandatory; no external-only accounts
/// are supported by this slice. Does not delete the user or game data.
/// </summary>
public sealed class ExternalIdentityRevocation(AuthDbContext db, IPasswordHasher passwords, TimeProvider clock)
{
    public async Task<ExternalUnlinkResult?> Unlink(Guid account, Guid session, ExternalIdentityKey key,
        string password, CancellationToken ct = default)
    {
        if (account == Guid.Empty || session == Guid.Empty || !key.Valid ||
            string.IsNullOrEmpty(password) || password.Length > 512) return null;
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == account && t.SessionId == session && t.ExpiresAt > now && t.User!.IsActive)
            .Select(t => t.User!.PasswordHash).SingleOrDefaultAsync(ct);
        if (string.IsNullOrEmpty(hash) || !passwords.Verify(password, hash)) return null;
        var deleted = await db.ExternalIdentities.Where(i => i.UserId == account && i.Provider == key.Provider &&
            i.Realm == key.Realm && i.Subject == key.Subject).ExecuteDeleteAsync(ct);
        if (deleted != 1) return null;

        // Explicit deletion as well as FK cascade: pending flows for this account cannot survive.
        var pending = db.ExternalLinkIntents.Where(i => i.UserId == account);
        await db.ExternalOAuthFlows.Where(f => pending.Any(i => i.CodeHash == f.LinkHash)).ExecuteDeleteAsync(ct);
        await pending.ExecuteDeleteAsync(ct);
        var revoked = await db.RefreshTokens.Where(t => t.UserId == account).ExecuteDeleteAsync(ct);
        db.ExternalIdentityAudits.Add(new ExternalIdentityAudit
        {
            UserId = account, SessionId = session, Provider = key.Provider, Realm = key.Realm,
            Action = "unlinked", OccurredUtc = now, RevokedSessions = revoked
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new(revoked);
    }
}
