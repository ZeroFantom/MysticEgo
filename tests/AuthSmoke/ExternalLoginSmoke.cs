using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Autorization.Configuration;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;
using Models.Autorization.Services;

internal static class ExternalLoginSmoke
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Current;
    }
    private sealed class VkFixture : HttpMessageHandler
    {
        public string State = "";
        public int Subject = 42;
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            var json = request.RequestUri?.AbsolutePath == "/oauth2/auth"
                ? JsonSerializer.Serialize(new { state = State, access_token = "fixture", user_id = Subject })
                : JsonSerializer.Serialize(new { user = new { user_id = Subject } });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
    public static async Task Run()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE external_login_flows");
        await AccountSecuritySchema.UpgradeAsync(db, default);
        await AccountSecuritySchema.UpgradeAsync(db, default);
        var user = new User { Login = "login-fixture", PasswordHash = "fixture" };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var key = new ExternalIdentityKey("vk", "oauth:123", "42");
        var store = new ExternalIdentityStore(db);
        await store.RecordVerifiedLink(user.Id, key);
        var clock = new Clock(DateTimeOffset.UtcNow.AddSeconds(1));
        var authOptions = new AuthorizationOptions { Jwt = new JwtOptions { SecretKey = new string('k', 64) } };
        var jwt = new JwtTokenService(authOptions.Jwt);
        var auth = new AuthService(db, new VersionedPasswordHasher(), jwt, authOptions,
            NullLogger<AuthService>.Instance, new DesktopPairing());
        var handler = new VkFixture();
        using var vk = new VkIdentityProof(handler);
        using var telegram = new TelegramOAuthExchange();
        var login = new ExternalLoginService(db, clock, new EphemeralDataProtectionProvider(), auth, telegram, vk);
        var config = new ExternalCallbackConfiguration("vk", "123", new Uri("https://example.invalid/login"));
        var browser = new string('b', 43);
        void Check(bool ok, string label) { if (!ok) throw new Exception("External login: " + label); }
        async Task<OAuthLinkStart> Start()
        {
            var flow = (await login.Start(config, browser))!;
            handler.State = flow.State;
            return flow;
        }
        var start = await Start();
        var stored = await db.ExternalLoginFlows.AsNoTracking().SingleAsync();
        Check(stored.StateHash != start.State && stored.BrowserHash != browser &&
            !stored.ProtectedSecrets.Contains(start.Nonce), "protected state");
        Check(await login.Complete(config, start.State, new string('c',43), "code", "device", null, null) is null &&
            handler.Calls == 0, "wrong browser");
        var result = await login.Complete(config, start.State, browser, "code", "device", null, null);
        Check(result?.User.Id == user.Id && handler.Calls == 2, "existing account issued");
        var principal = jwt.ValidateToken(result!.AccessToken);
        Check(principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value == user.Id.ToString() &&
            Guid.TryParse(principal.FindFirst("mystic_session")?.Value, out var sid) &&
            await db.RefreshTokens.AnyAsync(t => t.UserId == user.Id && t.SessionId == sid), "Mystic Ego session");
        Check(await login.Complete(config, start.State, browser, "code", "device", null, null) is null &&
            handler.Calls == 2, "one-use callback");
        Check(await db.ExternalIdentityAudits.CountAsync(a => a.UserId == user.Id && a.Action == "login") == 1, "audit");
        handler.Subject = 999;
        start = await Start();
        Check(await login.Complete(config, start.State, browser, "code", "device", null, null) is null &&
            await db.Users.CountAsync() == 1, "unknown subject no registration");
        handler.Subject = 42;
        start = await Start();
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, false));
        db.ChangeTracker.Clear();
        Check(await login.Complete(config, start.State, browser, "code", "device", null, null) is null, "inactive account");
        await db.Users.Where(u => u.Id == user.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
        start = await Start();
        await db.ExternalIdentities.Where(i => i.UserId == user.Id).ExecuteUpdateAsync(s => s.SetProperty(i => i.LinkedUtc, clock.Current.UtcDateTime.AddSeconds(1)));
        Check(await login.Complete(config, start.State, browser, "code", "device", null, null) is null, "relinked after flow start");
        start = await Start();
        clock.Current += ExternalLoginService.Lifetime;
        var calls = handler.Calls;
        Check(await login.Complete(config, start.State, browser, "code", "device", null, null) is null && handler.Calls == calls, "expired");
        Check(await login.PurgeExpired() == 1, "cleanup");
        Check(await db.RefreshTokens.CountAsync() == 1, "only verified login issued session");
        Console.WriteLine("External login purpose isolation, verified account, session and replay checks passed");
    }
}
