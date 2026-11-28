using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class ExternalIdentitySmoke
{
    public static async Task Run()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        // Simulate pre-existing Auth database without the new table, preserving existing accounts.
        var alice = new User { Login = "identity-alice", PasswordHash = "fixture" };
        var bob = new User { Login = "identity-bob", PasswordHash = "fixture" };
        db.Users.AddRange(alice, bob);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE external_identities");
        await AccountSecuritySchema.UpgradeAsync(db, default);
        await AccountSecuritySchema.UpgradeAsync(db, default);
        var store = new ExternalIdentityStore(db);
        var tg = new ExternalIdentityKey("telegram", "fixture", "123");
        var vk = new ExternalIdentityKey("vk", "fixture", "123");
        void Check(bool ok, string label) { if (!ok) throw new Exception("External identity: " + label); }
        Check(await store.ResolveLinkedAccount(tg) is null, "unknown subject");
        Check(await store.RecordVerifiedLink(alice.Id, tg) == ExternalIdentityLinkResult.Linked, "first link");
        Check(await store.RecordVerifiedLink(alice.Id, tg) == ExternalIdentityLinkResult.AlreadyLinked, "idempotent link");
        Check(await store.RecordVerifiedLink(bob.Id, tg) == ExternalIdentityLinkResult.Unavailable, "no account takeover");
        Check(await store.RecordVerifiedLink(alice.Id, tg with { Subject = "456" }) == ExternalIdentityLinkResult.Unavailable, "one link per namespace");
        Check(await store.RecordVerifiedLink(alice.Id, vk) == ExternalIdentityLinkResult.Linked, "separate providers");
        Check(await store.ResolveLinkedAccount(tg) == alice.Id && await store.ResolveLinkedAccount(vk) == alice.Id, "same account");
        Check(await store.ResolveLinkedAccount(tg with { Realm = "different" }) is null, "namespace isolation");
        Check(await store.ResolveLinkedAccount(tg with { Provider = "Telegram" }) is null, "noncanonical provider");
        Check(await store.RecordVerifiedLink(Guid.NewGuid(), tg with { Subject = "new" }) == ExternalIdentityLinkResult.Unavailable, "missing account");
        Check(await store.RecordVerifiedLink(bob.Id, tg with { Subject = "invalid\n" }) == ExternalIdentityLinkResult.Unavailable, "invalid subject");
        Check(await db.Users.CountAsync() == 2, "no implicit users");
        // Exercise actual database uniqueness independently of application prechecks.
        db.ChangeTracker.Clear();
        var duplicate = new ExternalIdentity { Provider = tg.Provider, Realm = tg.Realm, Subject = tg.Subject, UserId = bob.Id, LinkedUtc = DateTime.UtcNow };
        db.ExternalIdentities.Add(duplicate);
        try { await db.SaveChangesAsync(); throw new Exception("Duplicate external subject accepted"); }
        catch (DbUpdateException) { db.Entry(duplicate).State = EntityState.Detached; }
        var second = new ExternalIdentity { Provider = tg.Provider, Realm = tg.Realm, Subject = "second", UserId = alice.Id, LinkedUtc = DateTime.UtcNow };
        db.ExternalIdentities.Add(second);
        try { await db.SaveChangesAsync(); throw new Exception("Duplicate account namespace accepted"); }
        catch (DbUpdateException) { db.Entry(second).State = EntityState.Detached; }
        await db.Users.Where(x => x.Id == alice.Id).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        Check(await store.ResolveLinkedAccount(tg) is null, "disabled account denied");
        Check(await store.RecordVerifiedLink(alice.Id, tg) == ExternalIdentityLinkResult.Unavailable, "disabled link denied");
        await db.Users.Where(x => x.Id == alice.Id).ExecuteDeleteAsync();
        Check(!await db.ExternalIdentities.AnyAsync(x => x.UserId == alice.Id), "cascade on deletion");
        Check(await db.Users.AnyAsync(x => x.Id == bob.Id), "unrelated account preserved");
        Console.WriteLine("External identity schema, ownership and namespace isolation passed");
    }
}
