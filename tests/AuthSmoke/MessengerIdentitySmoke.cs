using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;
using Models.Autorization.Services;

internal static class MessengerIdentitySmoke
{
    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password) => "fixture:" + password;
        public bool Verify(string password, string hash) => Hash(password) == hash;
    }
    public static async Task Run()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var h = new Hasher();
        var a = new User { Login = "bot-a", PasswordHash = h.Hash("password") };
        var b = new User { Login = "bot-b", PasswordHash = h.Hash("other") };
        var sa = Guid.NewGuid(); var sb = Guid.NewGuid();
        db.Users.AddRange(a,b);
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = a.Id, SessionId = sa, Token = "a", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new RefreshToken { UserId = b.Id, SessionId = sb, Token = "b", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var store = new ExternalIdentityStore(db);
        var bot = new MessengerIdentityService(db, TimeProvider.System, h);
        var realm = new MessengerRealm("telegram","123");
        var sender = new VerifiedMessengerSender("telegram","123","42",true);
        var oauth = new ExternalIdentityKey("telegram","oidc:123","42");
        void Check(bool ok,string label){if(!ok)throw new Exception("Messenger identity: "+label);}
        Check(await store.RecordVerifiedLink(b.Id,oauth)==ExternalIdentityLinkResult.Linked,"OAuth fixture");
        Check(await bot.Resolve(realm,sender) is null,"OAuth does not authorize bot");
        Check(await bot.Start(a.Id,sa,realm,"wrong") is null,"password");
        var code=(await bot.Start(a.Id,sa,realm,"password"))!;
        Check(!await bot.Observe(realm,sender with { PrivateHumanChat=false },code),"group denied");
        Check(!await bot.Observe(realm,sender with { BotId="124" },code),"wrong bot");
        Check(!await bot.Observe(realm,sender with { Provider="vk" },code),"wrong provider");
        Check(await bot.Observe(realm,sender,code),"verified private sender");
        Check(await bot.Resolve(realm,sender) is null,"proof not link");
        Check(await bot.Candidate(b.Id,sb,realm,code) is null,"wrong owner");
        var proof=(await bot.Candidate(a.Id,sa,realm,code))!;
        Check(proof.Realm=="bot:123"&&proof.Subject=="42","namespace");
        Check(await bot.Confirm(a.Id,sa,realm,code,oauth,"password")==ExternalIdentityLinkResult.Unavailable,"OAuth key rejected");
        Check(await bot.Confirm(a.Id,sa,realm,code,proof,"bad")==ExternalIdentityLinkResult.Unavailable,"confirm password");
        Check(await bot.Confirm(a.Id,sa,realm,code,proof,"password")==ExternalIdentityLinkResult.Linked,"confirmed");
        Check(await bot.Confirm(a.Id,sa,realm,code,proof,"password")==ExternalIdentityLinkResult.Unavailable,"replay");
        Check(await bot.Resolve(realm,sender)==a.Id&&await store.ResolveLinkedAccount(oauth)==b.Id,"independent owners");
        Check(await bot.Resolve(new("telegram","124"),sender) is null,"configured bot isolation");
        Check(await bot.Resolve(realm,sender with {PrivateHumanChat=false}) is null,"private resolution");
        var conflict=(await bot.Start(b.Id,sb,realm,"other"))!;
        Check(await bot.Observe(realm,sender,conflict),"conflict candidate");
        Check(await bot.Confirm(b.Id,sb,realm,conflict,proof,"other")==ExternalIdentityLinkResult.Unavailable,"takeover denied");
        Check(await new ExternalIdentityRevocation(db,h,TimeProvider.System).Unlink(a.Id,sa,proof,"password") is not null,"bot unlink");
        Check(await bot.Resolve(realm,sender) is null&&await store.ResolveLinkedAccount(oauth)==b.Id,"revoked and independent");
        Console.WriteLine("Messenger identity namespace, private sender and confirmed linking checks passed");
    }
}
