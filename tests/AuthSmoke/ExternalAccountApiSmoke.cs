using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;
using Models.Autorization.Security;
using Api.Auth.Controllers;

internal static class ExternalAccountApiSmoke
{
    private sealed class FixtureHasher : IPasswordHasher
    {
        public string Hash(string password) => "fixture:" + password;
        public bool Verify(string password, string hash) => Hash(password) == hash;
    }
    public static async Task Run()
    {
        var opt = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(opt);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var hasher = new FixtureHasher();
        var a = new User { Login = "api-a", PasswordHash = hasher.Hash("password") };
        var b = new User { Login = "api-b", PasswordHash = hasher.Hash("other") };
        var sa = Guid.NewGuid(); var sb = Guid.NewGuid();
        db.Users.AddRange(a, b);
        db.RefreshTokens.AddRange(
            new RefreshToken { UserId = a.Id, SessionId = sa, Token = "a", ExpiresAt = DateTime.UtcNow.AddHours(1) },
            new RefreshToken { UserId = b.Id, SessionId = sb, Token = "b", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
        {
            ["ExternalIdentity:Telegram:Enabled"] = "false",
            ["ExternalIdentity:Telegram:ClientId"] = "123",
            ["ExternalIdentity:Telegram:ClientSecret"] = "fixture-secret",
            ["ExternalIdentity:Telegram:RedirectUri"] = "https://example.invalid/api/auth/external/telegram/callback"
        }).Build();
        var links = new ExternalLinkIntents(db, TimeProvider.System);
        var flows = new ExternalOAuthFlows(db, TimeProvider.System, new EphemeralDataProtectionProvider());
        using var telegram = new TelegramOAuthExchange();
        using var vk = new VkIdentityProof();
        var controller = new ExternalAccountsController(db, hasher, config, links, flows, new(flows, telegram, vk));
        void Context(Guid user, Guid session, string origin = "https://example.invalid", bool device = false)
        {
            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.ToString()), new("mystic_session", session.ToString()) };
            if (device) claims.Add(new("token_use", "device"));
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "fixture")) };
            http.Request.Headers.Origin = origin;
            controller.ControllerContext = new ControllerContext { HttpContext = http };
        }
        void Check(bool ok, string label) { if (!ok) throw new Exception("External account API: " + label); }
        Context(a.Id, sa);
        Check(await controller.Start(new("password"), default) is NotFoundResult, "disabled by default");
        config["ExternalIdentity:Telegram:Enabled"] = "true";
        Context(a.Id, sa, "https://wrong.invalid");
        Check(await controller.Start(new("password"), default) is StatusCodeResult { StatusCode: 403 }, "origin check");
        Context(a.Id, sa, device: true);
        Check(await controller.Start(new("password"), default) is UnauthorizedResult, "device denied");
        Context(a.Id, sb);
        Check(await controller.Start(new("password"), default) is UnauthorizedResult, "wrong session");
        Context(a.Id, sa);
        Check(await controller.Start(new("wrong"), default) is UnauthorizedResult, "reauthentication");
        Check(await controller.Start(new("password"), default) is OkObjectResult, "start");
        var cookie = controller.Response.Headers.SetCookie.ToString();
        Check(cookie.Contains("__Host-mystic-external-link=") && cookie.Contains("secure", StringComparison.OrdinalIgnoreCase) &&
            cookie.Contains("httponly", StringComparison.OrdinalIgnoreCase) && cookie.Contains("samesite=lax", StringComparison.OrdinalIgnoreCase), "secure cookie");
        Check(await db.ExternalOAuthFlows.CountAsync() == 1, "one flow");
        Context(a.Id, sa);
        Check(await controller.TelegramCallback(default) is BadRequestResult, "missing callback parameters");
        // Simulate a trusted proof already staged; real provider HTTP is covered by separate fixtures.
        var code = (await links.Start(a.Id, sa, "telegram", "oidc:123"))!;
        var proof = new ExternalIdentityKey("telegram", "oidc:123", "fixture-subject");
        Check(await links.ObserveVerifiedSubject(code, proof), "proof fixture");
        Context(b.Id, sb);
        Check(await controller.Candidate(new(code), default) is NotFoundResult, "other account cannot inspect");
        Context(a.Id, sa);
        Check(await controller.Candidate(new(code), default) is OkObjectResult, "candidate visible");
        Check(await controller.Confirm(new(code, proof.Provider, proof.Realm, proof.Subject, "wrong"), default) is UnauthorizedResult, "confirm reauth");
        Check(await controller.Confirm(new(code, proof.Provider, proof.Realm, "wrong", "password"), default) is ConflictResult, "exact candidate");
        Check(await controller.Confirm(new(code, proof.Provider, proof.Realm, proof.Subject, "password"), default) is NoContentResult, "confirmed");
        Check(await controller.Confirm(new(code, proof.Provider, proof.Realm, proof.Subject, "password"), default) is ConflictResult, "replay");
        Check(await new ExternalIdentityStore(db).ResolveLinkedAccount(proof) == a.Id, "account ownership");
        Check(await controller.List(default) is OkObjectResult, "own list");
        Console.WriteLine("External account controller configuration, session, origin and confirmation checks passed");
    }
}
