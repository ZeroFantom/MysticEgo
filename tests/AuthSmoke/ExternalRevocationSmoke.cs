using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;
using Models.Autorization.Services;

internal static class ExternalRevocationSmoke
{
    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "test:" + password;
        public bool Verify(string password, string hash) => Hash(password) == hash;
    }
    public static async Task Run()
    {
        var opt = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(opt);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE external_identity_audit");
        await AccountSecuritySchema.UpgradeAsync(db, default);
        await AccountSecuritySchema.UpgradeAsync(db, default);
        var h = new Hasher();
        var a = new User { Login = "revoke-a", PasswordHash = h.Hash("password") };
        var b = new User { Login = "revoke-b", PasswordHash = h.Hash("other") };
        var sa = Guid.NewGuid(); var sb = Guid.NewGuid(); var alternate = Guid.NewGuid();
        db.Users.AddRange(a, b);
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = a.Id, SessionId = sa, Token = "a", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new RefreshToken { UserId = a.Id, SessionId = alternate, Token = "alt", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new RefreshToken { UserId = a.Id, Token = "legacy", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new RefreshToken { UserId = b.Id, SessionId = sb, Token = "b", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var links = new ExternalLinkIntents(db, TimeProvider.System);
        var flows = new ExternalOAuthFlows(db, TimeProvider.System, new EphemeralDataProtectionProvider());
        var store = new ExternalIdentityStore(db);
        var revocation = new ExternalIdentityRevocation(db, h, TimeProvider.System);
        var tg = new ExternalIdentityKey("telegram", "oidc:123", "123");
        var vk = new ExternalIdentityKey("vk", "oauth:456", "456");
        void Check(bool ok, string label) { if (!ok) throw new Exception("External revocation: " + label); }
        async Task Link(Guid owner, Guid sid, ExternalIdentityKey key)
        {
            var code = (await links.Start(owner, sid, key.Provider, key.Realm))!;
            Check(await links.ObserveVerifiedSubject(code, key), "proof");
            Check(await links.Confirm(owner, sid, code, key) == ExternalIdentityLinkResult.Linked, "link");
        }
        await Link(a.Id, sa, tg);
        await Link(a.Id, sa, vk);
        await Link(b.Id, sb, tg with { Subject = "other" });
        var pending = (await links.Start(a.Id, alternate, "telegram", "oidc:123"))!;
        var browser = new string('b',43);
        var flow = (await flows.Start(a.Id, alternate, pending, browser, "telegram", "123", new Uri("https://example.invalid/callback")))!;
        var otherPending = (await links.Start(b.Id, sb, "vk", "oauth:456"))!;
        Check(await revocation.Unlink(b.Id, sb, tg, "other") is null, "ownership");
        Check(await revocation.Unlink(a.Id, sb, tg, "password") is null, "session ownership");
        Check(await revocation.Unlink(a.Id, sa, tg, "bad") is null, "password");
        Check(await store.ResolveLinkedAccount(tg) == a.Id, "no mutation on denial");
        // Force audit insertion failure; the identity and sessions must roll back.
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_unlink BEFORE INSERT ON external_identity_audit WHEN NEW.Action = 'unlinked' BEGIN SELECT RAISE(ABORT, 'fixture'); END");
        try { await revocation.Unlink(a.Id, sa, tg, "password"); throw new Exception("Audit failure accepted"); }
        catch (DbUpdateException) { db.ChangeTracker.Clear(); }
        Check(await store.ResolveLinkedAccount(tg) == a.Id, "rollback identity");
        Check(await db.RefreshTokens.CountAsync(t => t.UserId == a.Id) == 3, "rollback sessions");
        Check(await db.ExternalOAuthFlows.CountAsync() == 1, "rollback pending");
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_unlink");
        var result = await revocation.Unlink(a.Id, sa, tg, "password");
        Check(result?.RevokedSessions == 3, "all own refresh sessions");
        Check(await store.ResolveLinkedAccount(tg) is null, "identity removed");
        Check(await store.ResolveLinkedAccount(vk) == a.Id, "other provider retained");
        Check(await store.ResolveLinkedAccount(tg with { Subject = "other" }) == b.Id, "other account retained");
        Check(await db.Users.CountAsync() == 2, "accounts retained");
        Check(!await db.RefreshTokens.AnyAsync(t => t.UserId == a.Id), "sessions gone");
        Check(await db.RefreshTokens.AnyAsync(t => t.SessionId == sb), "other session");
        Check(!await db.ExternalLinkIntents.AnyAsync(t => t.UserId == a.Id), "pending removed");
        Check(await db.ExternalLinkIntents.AnyAsync(t => t.UserId == b.Id), "other pending");
        Check(await flows.Claim(flow.State, browser, "telegram") is null, "callback denied");
        Check(!await links.ObserveVerifiedSubject(pending, tg), "old proof denied");
        Check(await links.ObserveVerifiedSubject(otherPending, vk with { Subject = "other" }), "other proof allowed");
        Check(await revocation.Unlink(a.Id, sa, tg, "password") is null, "replay");
        Check(await db.ExternalIdentityAudits.CountAsync(x => x.UserId == a.Id && x.Action == "linked") == 2, "link audit");
        var audit = await db.ExternalIdentityAudits.SingleAsync(x => x.UserId == a.Id && x.Action == "unlinked");
        Check(audit.RevokedSessions == 3 && audit.SessionId == sa && audit.Provider == "telegram", "unlink audit");
        Console.WriteLine("External identity revocation, transactional audit and session isolation passed");
    }
}
