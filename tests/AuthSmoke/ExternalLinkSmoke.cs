using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class ExternalLinkSmoke
{
    private sealed class Clock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Current;
    }

    public static async Task Run()
    {
        var clock = new Clock(DateTimeOffset.UtcNow);
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var a = new User { Login = "link-a", PasswordHash = "fixture" };
        var b = new User { Login = "link-b", PasswordHash = "fixture" };
        var sa = Guid.NewGuid(); var sb = Guid.NewGuid(); var alternate = Guid.NewGuid();
        db.Users.AddRange(a, b);
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = a.Id, SessionId = sa, Token = "a", ExpiresAt = clock.Current.UtcDateTime.AddHours(1) },
            new RefreshToken { UserId = b.Id, SessionId = sb, Token = "b", ExpiresAt = clock.Current.UtcDateTime.AddHours(1) },
            new RefreshToken { UserId = a.Id, SessionId = alternate, Token = "alternate", ExpiresAt = clock.Current.UtcDateTime.AddHours(1) });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE external_link_intents");
        await AccountSecuritySchema.UpgradeAsync(db, default);
        await AccountSecuritySchema.UpgradeAsync(db, default);
        var links = new ExternalLinkIntents(db, clock);
        var store = new ExternalIdentityStore(db);
        var tg = new ExternalIdentityKey("telegram", "test", "123");
        var vk = new ExternalIdentityKey("vk", "test", "123");
        const ExternalIdentityLinkResult denied = ExternalIdentityLinkResult.Unavailable;
        void Check(bool ok, string label) { if (!ok) throw new Exception("Link intent: " + label); }
        Check(await links.Start(a.Id, sb, "telegram", "test") is null, "wrong session owner");
        Check(await links.Start(a.Id, sa, "unknown", "test") is null, "unknown provider");
        var code = (await links.Start(a.Id, sa, "telegram", "test"))!;
        Check(code.Length == 43 && !await db.ExternalLinkIntents.AnyAsync(x => x.CodeHash == code), "only hash persisted");
        Check(await links.Confirm(a.Id, sa, code, tg) == denied, "unverified");
        Check(!await links.ObserveVerifiedSubject(code, vk), "wrong provider");
        Check(!await links.ObserveVerifiedSubject(code, tg with { Realm = "other" }), "wrong realm");
        Check(await links.ObserveVerifiedSubject(code, tg), "proof recorded");
        Check(!await links.ObserveVerifiedSubject(code, tg with { Subject = "attacker" }), "candidate immutable");
        Check(await store.ResolveLinkedAccount(tg) is null, "proof alone not linked");
        Check(await links.Candidate(b.Id, sb, code) is null, "candidate isolation");
        Check(await links.Candidate(a.Id, alternate, code) is null, "session isolation");
        Check(await links.Candidate(a.Id, sa, code) == tg, "candidate display");
        Check(await links.Confirm(a.Id, alternate, code, tg) == denied, "wrong session confirm");
        Check(await links.Confirm(b.Id, sb, code, tg) == denied, "wrong account confirm");
        Check(await links.Confirm(a.Id, sa, code, tg with { Subject = "attacker" }) == denied, "candidate mismatch");
        Check(await links.Confirm(a.Id, sa, code, tg) == ExternalIdentityLinkResult.Linked, "confirmed link");
        Check(await links.Confirm(a.Id, sa, code, tg) == denied, "replay rejected");
        Check(await store.ResolveLinkedAccount(tg) == a.Id, "correct account");
        var conflict = (await links.Start(b.Id, sb, "telegram", "test"))!;
        Check(await links.ObserveVerifiedSubject(conflict, tg), "conflicting proof staged");
        Check(await links.Confirm(b.Id, sb, conflict, tg) == denied, "existing ownership protected");
        Check(await links.Candidate(b.Id, sb, conflict) is null, "conflict consumed");
        Check(await store.ResolveLinkedAccount(tg) == a.Id, "owner unchanged");
        var expired = (await links.Start(a.Id, sa, "vk", "test"))!;
        Check(await links.ObserveVerifiedSubject(expired, vk), "expiry fixture");
        clock.Current += ExternalLinkIntents.Lifetime;
        Check(await links.Candidate(a.Id, sa, expired) is null, "expired candidate");
        Check(await links.Confirm(a.Id, sa, expired, vk) == denied, "expired confirm");
        Check(await links.PurgeExpired() == 1, "expiry cleanup");
        var revoked = (await links.Start(a.Id, sa, "vk", "test"))!;
        Check(await links.ObserveVerifiedSubject(revoked, vk), "revoke fixture");
        await db.RefreshTokens.Where(t => t.SessionId == sa).ExecuteDeleteAsync();
        Check(await links.Confirm(a.Id, sa, revoked, vk) == denied, "session revoked");
        Check(await links.Start(a.Id, sa, "vk", "test") is null, "revoked session start");
        Check(await store.ResolveLinkedAccount(vk) is null, "no link after revoke");
        var disabled = (await links.Start(a.Id, alternate, "vk", "test"))!;
        await db.Users.Where(u => u.Id == a.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        Check(!await links.ObserveVerifiedSubject(disabled, vk), "disabled account proof denied");
        Console.WriteLine("External account link intent, proof, session binding and replay checks passed");
    }
}
