using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class OAuthCallbackSmoke
{
    private sealed class TelegramFixture(RSA rsa) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string Nonce { get; set; } = "";
        public bool BadKey { get; set; }
        public bool Redirect { get; set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            if (request.RequestUri?.AbsoluteUri == TelegramOAuthExchange.TokenEndpoint)
            {
                if (request.Method != HttpMethod.Post || request.Headers.Authorization?.Scheme != "Basic" ||
                    request.Headers.Authorization.Parameter != Convert.ToBase64String(Encoding.UTF8.GetBytes("123:fixture-secret")))
                    throw new Exception("Telegram client authentication");
                var form = await request.Content!.ReadAsStringAsync(ct);
                if (!form.Contains("grant_type=authorization_code") || !form.Contains("code_verifier=") ||
                    !form.Contains("redirect_uri=https%3A%2F%2Fexample.invalid%2Fcallback") || !form.Contains("code=fixture"))
                    throw new Exception("Telegram exchange fields");
                if (Redirect) return new HttpResponseMessage(HttpStatusCode.Found);
                var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
                var token = new JwtSecurityToken(TelegramIdentityProof.Issuer, "123",
                    [new Claim("sub", "verified-telegram"), new Claim("nonce", Nonce),
                     new Claim("iat", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                    expires: DateTime.UtcNow.AddMinutes(3), signingCredentials: new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
                return Json(JsonSerializer.Serialize(new { id_token = new JwtSecurityTokenHandler().WriteToken(token), access_token = "ignored" }));
            }
            if (request.RequestUri?.AbsoluteUri != TelegramIdentityProof.Jwks || request.Method != HttpMethod.Get ||
                request.Headers.Authorization is not null) throw new Exception("Unexpected JWKS request");
            using var wrong = RSA.Create(2048);
            var p = (BadKey ? wrong : rsa).ExportParameters(false);
            return Json(JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "fixture",
                use = "sig", alg = "RS256", n = Base64UrlEncoder.Encode(p.Modulus!), e = Base64UrlEncoder.Encode(p.Exponent!) } } }));
        }
    }
    private sealed class VkFixture : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Json(request.RequestUri?.AbsolutePath == "/oauth2/auth"
                ? JsonSerializer.Serialize(new { state = State, access_token = "fixture", user_id = 42 })
                : """{"user":{"user_id":42}}"""));
        }
        public string State { get; set; } = "";
    }
    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    public static async Task Run()
    {
        var options = new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db = new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        var user = new User { Login = "callback-fixture", PasswordHash = "fixture" };
        var session = Guid.NewGuid();
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken { UserId = user.Id, SessionId = session, Token = "fixture", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        await db.SaveChangesAsync();
        var links = new ExternalLinkIntents(db, TimeProvider.System);
        var flows = new ExternalOAuthFlows(db, TimeProvider.System, new EphemeralDataProtectionProvider());
        using var rsa = RSA.Create(2048);
        var fixture = new TelegramFixture(rsa);
        using var telegram = new TelegramOAuthExchange(fixture);
        var vkFixture = new VkFixture();
        using var vk = new VkIdentityProof(vkFixture);
        var callbacks = new ExternalOAuthCallbacks(flows, telegram, vk);
        var redirect = new Uri("https://example.invalid/callback");
        var config = new ExternalCallbackConfiguration("telegram", "123", redirect, "fixture-secret");
        var browser = new string('b', 43);
        void Check(bool ok, string label) { if (!ok) throw new Exception("OAuth callback: " + label); }
        async Task<(string code, OAuthLinkStart start)> Start(string provider = "telegram")
        {
            var realm = provider == "telegram" ? "oidc:123" : "oauth:123";
            var code = (await links.Start(user.Id, session, provider, realm))!;
            var start = (await flows.Start(user.Id, session, code, browser, provider, "123", redirect))!;
            fixture.Nonce = start.Nonce;
            return (code, start);
        }
        var first = await Start();
        var url = TelegramOAuthExchange.AuthorizationUri("123", redirect, first.start)!;
        Check(url.Host == "oauth.telegram.org" && url.AbsolutePath == "/auth" &&
            url.Query.Contains("code_challenge_method=S256") && url.Query.Contains("nonce=" + first.start.Nonce) &&
            !url.Query.Contains("fixture-secret") && !url.Query.Contains("phone"), "minimal authorization URL");
        Check(!await callbacks.Complete(config, first.start.State, new string('c', 43), "fixture") && fixture.Calls == 0, "wrong browser no exchange");
        Check(await callbacks.Complete(config, first.start.State, browser, "fixture") && fixture.Calls == 2, "verified callback");
        Check(!await callbacks.Complete(config, first.start.State, browser, "fixture") && fixture.Calls == 2, "replay no exchange");
        var proof = new ExternalIdentityKey("telegram", "oidc:123", "verified-telegram");
        Check(await links.Candidate(user.Id, session, first.code) == proof, "staged proof");
        Check(await new ExternalIdentityStore(db).ResolveLinkedAccount(proof) is null, "no automatic link");
        Check(await links.Confirm(user.Id, session, first.code, proof) == ExternalIdentityLinkResult.Linked, "explicit confirmation");
        var bad = await Start(); fixture.BadKey = true;
        Check(!await callbacks.Complete(config, bad.start.State, browser, "fixture") &&
            await links.Candidate(user.Id, session, bad.code) is null, "invalid signing key");
        fixture.BadKey = false;
        var nonce = await Start(); fixture.Nonce = "wrong";
        Check(!await callbacks.Complete(config, nonce.start.State, browser, "fixture"), "bad nonce");
        var changed = await Start(); var before = fixture.Calls;
        Check(!await callbacks.Complete(new("telegram", "124", redirect, "fixture-secret"), changed.start.State, browser, "fixture") &&
            fixture.Calls == before, "config change no exchange");
        var denied = await Start(); fixture.Redirect = true; before = fixture.Calls;
        Check(!await callbacks.Complete(config, denied.start.State, browser, "fixture") &&
            fixture.Calls == before + 1, "redirect rejected");
        fixture.Redirect = false;
        var vkStart = await Start("vk"); vkFixture.State = vkStart.start.State;
        Check(await callbacks.Complete(new("vk", "123", redirect), vkStart.start.State, browser, "fixture", "device") &&
            vkFixture.Calls == 2, "VK callback staging");
        Check(await links.Candidate(user.Id, session, vkStart.code) == new ExternalIdentityKey("vk", "oauth:123", "42"), "VK verified candidate");
        Console.WriteLine("OAuth callback orchestration and Telegram exchange/JWKS fixtures passed");
    }
}
