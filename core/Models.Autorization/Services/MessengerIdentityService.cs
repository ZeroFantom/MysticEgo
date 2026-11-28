using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Security;

namespace Models.Autorization.Services;

/// <summary>Server-configured bot/community identity. Never accept this configuration from a user.</summary>
public sealed record MessengerRealm(string Provider, string BotId)
{
    public bool Valid => Provider is "telegram" or "vk" && TelegramIdentityProof.PositiveId(BotId);
    internal string Realm => "bot:" + BotId;
}

/// <summary>Normalized private sender from an authenticated provider event, NOT a public request DTO.
/// A trusted adapter must verify event origin, bot recipient, freshness, replay and user/chat type.</summary>
public sealed record VerifiedMessengerSender(string Provider, string BotId, string UserId, bool PrivateHumanChat);

/// <summary>Reference linking and resolution service; no webhook, transport or HTTP exposure.
/// A bot link is independent of a social OAuth link, and cannot issue an OAuth login session.</summary>
public sealed class MessengerIdentityService(AuthDbContext db, TimeProvider clock, IPasswordHasher passwords)
{
    private ExternalLinkIntents Intents => new(db, clock);
    private static ExternalIdentityKey? Key(MessengerRealm realm, VerifiedMessengerSender sender) =>
        realm.Valid && sender.PrivateHumanChat && sender.Provider == realm.Provider &&
        sender.BotId == realm.BotId && TelegramIdentityProof.PositiveId(sender.UserId)
        ? new(realm.Provider, realm.Realm, sender.UserId) : null;

    private async Task<bool> Reauthenticate(Guid account, Guid session, string password, CancellationToken ct)
    {
        if (account == Guid.Empty || session == Guid.Empty || string.IsNullOrEmpty(password) || password.Length > 512)
            return false;
        var now = clock.GetUtcNow().UtcDateTime;
        var hash = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == account && t.SessionId == session && t.ExpiresAt > now && t.User!.IsActive)
            .Select(t => t.User!.PasswordHash).SingleOrDefaultAsync(ct);
        return !string.IsNullOrEmpty(hash) && passwords.Verify(password, hash);
    }

    public async Task<string?> Start(Guid account, Guid session, MessengerRealm realm, string password, CancellationToken ct = default)
    {
        if (!realm.Valid || !await Reauthenticate(account, session, password, ct)) return null;
        return await Intents.Start(account, session, realm.Provider, realm.Realm, ct);
    }

    /// <summary>Only trusted private bot adapter may stage proof. No association or session yet.</summary>
    public Task<bool> Observe(MessengerRealm realm, VerifiedMessengerSender sender, string code, CancellationToken ct = default)
    {
        var key = Key(realm, sender);
        return key is null ? Task.FromResult(false) : Intents.ObserveVerifiedSubject(code, key, ct);
    }

    public async Task<ExternalIdentityKey?> Candidate(Guid account, Guid session, MessengerRealm realm,
        string code, CancellationToken ct = default)
    {
        if (!realm.Valid) return null;
        var candidate = await Intents.Candidate(account, session, code, ct);
        return candidate?.Provider == realm.Provider && candidate.Realm == realm.Realm ? candidate : null;
    }

    public async Task<ExternalIdentityLinkResult> Confirm(Guid account, Guid session, MessengerRealm realm,
        string code, ExternalIdentityKey displayed, string password, CancellationToken ct = default)
    {
        if (!realm.Valid || displayed.Provider != realm.Provider || displayed.Realm != realm.Realm ||
            !TelegramIdentityProof.PositiveId(displayed.Subject) ||
            !await Reauthenticate(account, session, password, ct)) return ExternalIdentityLinkResult.Unavailable;
        return await Intents.Confirm(account, session, code, displayed, ct);
    }

    /// <summary>Resolve on every inbound event and again before external delivery; do not cache grants.
    /// This is identity resolution, not authorization to use a character or contact an NPC.</summary>
    public Task<Guid?> Resolve(MessengerRealm realm, VerifiedMessengerSender sender, CancellationToken ct = default)
    {
        var key = Key(realm, sender);
        return key is null ? Task.FromResult<Guid?>(null) : new ExternalIdentityStore(db).ResolveLinkedAccount(key, ct);
    }
}
