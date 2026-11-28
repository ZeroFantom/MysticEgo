using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

public sealed record ExternalIdentityKey(string Provider, string Realm, string Subject)
{
    /// <summary>Canonical configured provider/namespace plus an opaque verified subject.</summary>
    public bool Valid => (Provider is "telegram" or "vk") &&
        ValidPart(Realm) && ValidPart(Subject);
    private static bool ValidPart(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= 128 &&
        value.All(c => c is >= '!' and <= '~');
}

public enum ExternalIdentityLinkResult { Linked, AlreadyLinked, Unavailable }

/// <summary>
/// Persistence foundation only. No HTTP endpoints, login, provider verification or session issuance.
/// Callers must independently verify provider proof, configured realm, authenticated account,
/// recent reauthentication and explicit consent, and consume a bound one-use linking challenge.
/// Never construct a verified key directly from untrusted client input.
/// </summary>
public sealed class ExternalIdentityStore(AuthDbContext db)
{
    /// <summary>Only resolves an already linked, active account. No implicit account creation or merge.</summary>
    public async Task<Guid?> ResolveLinkedAccount(ExternalIdentityKey key, CancellationToken ct = default)
    {
        if (!key.Valid) return null;
        return await (from identity in db.ExternalIdentities.AsNoTracking()
                      join user in db.Users.AsNoTracking() on identity.UserId equals user.Id
                      where identity.Provider == key.Provider && identity.Realm == key.Realm &&
                            identity.Subject == key.Subject && user.IsActive
                      select (Guid?)user.Id).SingleOrDefaultAsync(ct);
    }

    /// <summary>
    /// Insert-only persistence operation for an already verified, explicitly confirmed association.
    /// A database uniqueness violation from a concurrent insert is a failed write, never a successful
    /// account switch. The caller must discard the failed unit of work and handle conflicts safely.
    /// </summary>
    public async Task<ExternalIdentityLinkResult> RecordVerifiedLink(Guid accountId, ExternalIdentityKey key,
        CancellationToken ct = default)
    {
        if (accountId == Guid.Empty || !key.Valid ||
            !await db.Users.AsNoTracking().AnyAsync(u => u.Id == accountId && u.IsActive, ct))
            return ExternalIdentityLinkResult.Unavailable;
        var existing = await db.ExternalIdentities.AsNoTracking().SingleOrDefaultAsync(
            x => x.Provider == key.Provider && x.Realm == key.Realm && x.Subject == key.Subject, ct);
        if (existing is not null)
            return existing.UserId == accountId ? ExternalIdentityLinkResult.AlreadyLinked : ExternalIdentityLinkResult.Unavailable;
        if (await db.ExternalIdentities.AsNoTracking().AnyAsync(
            x => x.UserId == accountId && x.Provider == key.Provider && x.Realm == key.Realm, ct))
            return ExternalIdentityLinkResult.Unavailable;
        db.ExternalIdentities.Add(new ExternalIdentity
        {
            UserId = accountId, Provider = key.Provider, Realm = key.Realm,
            Subject = key.Subject, LinkedUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return ExternalIdentityLinkResult.Linked;
    }
}
