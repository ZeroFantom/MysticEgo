using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Services;

internal static class ProviderProofSmoke
{
    private sealed class Fixture(string[] replies) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var index = Calls++;
            var path = index == 0 ? "/oauth2/auth" : "/oauth2/user_info";
            if (request.RequestUri?.AbsoluteUri != "https://id.vk.com" + path || request.Method != HttpMethod.Post)
                throw new Exception("Unexpected VK endpoint");
            var body = await request.Content!.ReadAsStringAsync(ct);
            if (!body.Contains("client_id=123") || index == 0 && (!body.Contains("code_verifier=") || !body.Contains("device_id=fixture")) ||
                index == 1 && !body.Contains("access_token=fixture-token"))
                throw new Exception("Unexpected VK fields");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(replies[index]) };
        }
    }

    public static async Task Run()
    {
        using var rsa = RSA.Create(2048);
        using var wrong = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "fixture" };
        var other = new RsaSecurityKey(wrong) { KeyId = "fixture" };
        var now = DateTime.UtcNow;
        string Token(string issuer, string audience, string nonce, DateTime iat, DateTime expiry, SecurityKey signer)
        {
            var token = new JwtSecurityToken(issuer, audience,
                [new Claim("sub", "subject-1"), new Claim("nonce", nonce),
                 new Claim("iat", new DateTimeOffset(iat).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)],
                expires: expiry, signingCredentials: new SigningCredentials(signer, SecurityAlgorithms.RsaSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        void Check(bool condition, string label) { if (!condition) throw new Exception("Provider proof: " + label); }
        var good = Token(TelegramIdentityProof.Issuer, "123", "nonce", now, now.AddMinutes(5), key);
        Check(TelegramIdentityProof.Verify(good, "123", "nonce", [key]) ==
            new ExternalIdentityKey("telegram", "oidc:123", "subject-1"), "valid Telegram proof");
        Check(TelegramIdentityProof.Verify(good, "123", "wrong", [key]) is null, "nonce");
        Check(TelegramIdentityProof.Verify(good, "124", "nonce", [key]) is null, "audience");
        Check(TelegramIdentityProof.Verify(good, "123", "nonce", [other]) is null, "signature");
        Check(TelegramIdentityProof.Verify(Token("https://other.invalid", "123", "nonce", now, now.AddMinutes(5), key), "123", "nonce", [key]) is null, "issuer");
        Check(TelegramIdentityProof.Verify(Token(TelegramIdentityProof.Issuer, "123", "nonce", now.AddMinutes(-10), now.AddMinutes(5), key), "123", "nonce", [key]) is null, "stale iat");
        Check(TelegramIdentityProof.Verify(Token(TelegramIdentityProof.Issuer, "123", "nonce", now.AddMinutes(-2), now.AddMinutes(-1), key), "123", "nonce", [key]) is null, "expiry");
        Check(TelegramIdentityProof.Verify("broken", "123", "nonce", [key]) is null, "malformed");

        const string exchanged = """{"state":"state","access_token":"fixture-token","user_id":42}""";
        const string profile = """{"user":{"user_id":"42"}}""";
        async Task<ExternalIdentityKey?> Exchange(VkIdentityProof proof, string callback = "state") =>
            await proof.Exchange("123", new Uri("https://example.invalid/callback"), "state", callback,
                new string('v', 64), "code", "fixture");
        var fixture = new Fixture([exchanged, profile]);
        using var vk = new VkIdentityProof(fixture);
        Check(await Exchange(vk, "wrong") is null && fixture.Calls == 0, "VK callback state");
        Check(await Exchange(vk) == new ExternalIdentityKey("vk", "oauth:123", "42") && fixture.Calls == 2, "VK server exchange");
        using var mismatch = new VkIdentityProof(new Fixture([exchanged, """{"user":{"user_id":"43"}}"""]));
        Check(await Exchange(mismatch) is null, "VK mismatched subject");
        using var duplicate = new VkIdentityProof(new Fixture([exchanged, """{"user":{"user_id":"42","user_id":"43"}}"""]));
        Check(await Exchange(duplicate) is null, "VK ambiguous response");
        using var error = new VkIdentityProof(new Fixture(["""{"error":"invalid_grant"}"""]));
        Check(await Exchange(error) is null, "VK provider denial");
        Console.WriteLine("Telegram signed proof and VK server-side exchange checks passed");
    }
}
