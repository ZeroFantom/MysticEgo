using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class ExternalOAuthSmoke
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
        await db.Database.ExecuteSqlRawAsync("DROP TABLE external_oauth_flows");
        await AccountSecuritySchema.UpgradeAsync(db, default);
        await AccountSecuritySchema.UpgradeAsync(db, default);
        var account = new User { Login = "oauth-fixture", PasswordHash = "fixture" };
        var session = Guid.NewGuid();
        db.Users.Add(account);
        db.RefreshTokens.Add(new RefreshToken { UserId = account.Id, SessionId = session, Token = "fixture", ExpiresAt = clock.Current.UtcDateTime.AddHours(1) });
        await db.SaveChangesAsync();
        var links = new ExternalLinkIntents(db, clock);
        var protection = new EphemeralDataProtectionProvider();
        var flows = new ExternalOAuthFlows(db, clock, protection);
        var browser = new string('b', 43);
        var redirect = new Uri("https://example.invalid/callback");
        var code = (await links.Start(account.Id, session, "telegram", "oidc:123"))!;
        void Check(bool ok, string label) { if (!ok) throw new Exception("OAuth link: " + label); }
        Check(await flows.Start(account.Id, Guid.NewGuid(), code, browser, "telegram", "123", redirect) is null, "wrong session");
        Check(await flows.Start(account.Id, session, code, browser, "vk", "123", redirect) is null, "wrong provider");
        Check(await flows.Start(account.Id, session, code, browser, "telegram", "456", redirect) is null, "wrong client");
        Check(await flows.Start(account.Id, session, code, browser, "telegram", "123", new Uri("http://example.invalid")) is null, "insecure redirect");
        var started = (await flows.Start(account.Id, session, code, browser, "telegram", "123", redirect))!;
        Check(started.State.Length == 43 && started.Challenge.Length == 43 && started.Nonce.Length == 43, "challenge format");
        Check(await flows.Start(account.Id, session, code, browser, "telegram", "123", redirect) is null, "duplicate flow");
        var stored = await db.ExternalOAuthFlows.AsNoTracking().SingleAsync();
        Check(stored.StateHash != started.State && stored.BrowserHash != browser && !stored.ProtectedSecrets.Contains(code), "secrets not plain");
        Check(await flows.Claim(started.State, new string('c', 43), "telegram") is null, "browser binding");
        Check(await flows.Claim(started.State, browser, "vk") is null, "provider binding");
        var claim = (await flows.Claim(started.State, browser, "telegram"))!;
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(claim.Verifier))).TrimEnd('=').Replace('+','-').Replace('/','_');
        Check(claim.Nonce == started.Nonce && challenge == started.Challenge && claim.RedirectUri == redirect, "stored OAuth values");
        Check(await flows.Claim(started.State, browser, "telegram") is null, "replay");
        var proof = new ExternalIdentityKey("telegram", "oidc:123", "subject");
        Check(!await flows.StageVerifiedProof(claim, proof with { Realm = "oidc:456" }), "proof namespace");
        Check(await flows.StageVerifiedProof(claim, proof), "verified candidate");
        Check(await new ExternalIdentityStore(db).ResolveLinkedAccount(proof) is null, "no automatic linking");
        Check(await links.Candidate(account.Id, session, code) == proof, "candidate available to original session");
        Check(await links.Confirm(account.Id, session, code, proof) == ExternalIdentityLinkResult.Linked, "explicit confirmation");
        var second = (await links.Start(account.Id, session, "vk", "oauth:123"))!;
        var expired = (await flows.Start(account.Id, session, second, browser, "vk", "123", redirect))!;
        clock.Current += ExternalLinkIntents.Lifetime;
        Check(await flows.Claim(expired.State, browser, "vk") is null, "expired flow");
        Check(await flows.PurgeExpired() == 1, "cleanup");
        var third = (await links.Start(account.Id, session, "vk", "oauth:123"))!;
        var revoked = (await flows.Start(account.Id, session, third, browser, "vk", "123", redirect))!;
        await db.RefreshTokens.Where(t => t.SessionId == session).ExecuteDeleteAsync();
        Check(await flows.Claim(revoked.State, browser, "vk") is null, "revoked session");
        Console.WriteLine("External OAuth state, PKCE, browser binding and one-use claim checks passed");
    }
}
