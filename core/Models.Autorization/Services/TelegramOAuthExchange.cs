using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Models.Autorization.Services;

/// <summary>
/// Telegram authorization-code transport. Endpoints are fixed and redirects disabled.
/// Client secret and callback configuration originate exclusively from trusted server config.
/// No access tokens or ID tokens are persisted or returned to the browser.
/// </summary>
public sealed class TelegramOAuthExchange : IDisposable
{
    public const string TokenEndpoint = TelegramIdentityProof.Issuer + "/token";
    public const string AuthorizationEndpoint = TelegramIdentityProof.Issuer + "/auth";
    private readonly HttpClient http;

    public TelegramOAuthExchange() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
    public TelegramOAuthExchange(HttpMessageHandler handler) =>
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536 };

    public static Uri? AuthorizationUri(string clientId, Uri redirect, OAuthLinkStart start)
    {
        if (!TelegramIdentityProof.PositiveId(clientId) || !ValidRedirect(redirect) ||
            !Pkce(start.State) || !Pkce(start.Challenge) || !Pkce(start.Nonce)) return null;
        var fields = new Dictionary<string, string>
        {
            ["client_id"] = clientId, ["redirect_uri"] = redirect.AbsoluteUri,
            ["response_type"] = "code", ["scope"] = "openid profile",
            ["state"] = start.State, ["nonce"] = start.Nonce,
            ["code_challenge"] = start.Challenge, ["code_challenge_method"] = "S256"
        };
        return new Uri(AuthorizationEndpoint + "?" + string.Join("&", fields.Select(p =>
            Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value))));
    }

    public async Task<ExternalIdentityKey?> Exchange(OAuthLinkClaim claim, string clientSecret,
        string code, CancellationToken ct = default)
    {
        if (claim.Provider != "telegram" || !TelegramIdentityProof.PositiveId(claim.ClientId) ||
            !ValidRedirect(claim.RedirectUri) || !Pkce(claim.Verifier) || !Pkce(claim.Nonce) ||
            !Bounded(code, 4096) || !Bounded(clientSecret, 1024)) return null;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(claim.ClientId + ":" + clientSecret)));
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = claim.ClientId,
                ["redirect_uri"] = claim.RedirectUri.AbsoluteUri, ["code_verifier"] = claim.Verifier,
                ["code"] = code
            });
            using var response = await http.SendAsync(request, deadline.Token);
            if (!response.IsSuccessStatusCode) return null;
            using var tokenBody = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(deadline.Token));
            var body = tokenBody.RootElement;
            if (!TelegramIdentityProof.UniqueObject(body) || body.TryGetProperty("error", out _) ||
                TelegramIdentityProof.Text(body, "id_token") is not { Length: > 0 and <= 16384 } token)
                return null;

            // Fetch current keys from the fixed issuer, never a jku/x5u supplied by a token.
            // No Basic credentials on this separate request. Per-exchange fetch deliberately
            // avoids stale-key fallback; bounded shared cache and rate limits are a release gate.
            using var jwksResponse = await http.GetAsync(TelegramIdentityProof.Jwks, deadline.Token);
            if (!jwksResponse.IsSuccessStatusCode) return null;
            var jwks = await jwksResponse.Content.ReadAsStringAsync(deadline.Token);
            using var parsed = JsonDocument.Parse(jwks);
            if (!TelegramIdentityProof.UniqueObject(parsed.RootElement) ||
                !parsed.RootElement.TryGetProperty("keys", out var keys) ||
                keys.ValueKind != JsonValueKind.Array || keys.GetArrayLength() is 0 or > 32 ||
                keys.EnumerateArray().Any(k => !TelegramIdentityProof.UniqueObject(k))) return null;
            var set = new JsonWebKeySet(jwks);
            var signing = set.Keys.Where(k => k.Kty == "RSA" &&
                (string.IsNullOrEmpty(k.Use) || k.Use == "sig") &&
                (string.IsNullOrEmpty(k.Alg) || k.Alg == "RS256") &&
                !string.IsNullOrEmpty(k.Kid) && !string.IsNullOrEmpty(k.N) && !string.IsNullOrEmpty(k.E) &&
                string.IsNullOrEmpty(k.D)).ToArray();
            if (signing.Length == 0 || signing.Select(k => k.Kid).Distinct(StringComparer.Ordinal).Count() != signing.Length)
                return null;
            return TelegramIdentityProof.Verify(token, claim.ClientId, claim.Nonce, signing);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or ArgumentException or
            InvalidOperationException or SecurityTokenException or FormatException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return null; }
    }

    private static bool ValidRedirect(Uri? uri) => uri is { IsAbsoluteUri: true } &&
        uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 &&
        uri.AbsoluteUri.Length <= 2048;
    private static bool Bounded(string? value, int limit) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= limit && !value.Any(char.IsControl);
    private static bool Pkce(string? value) => value is { Length: >= 43 and <= 128 } &&
        value.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.' or '~');
    public void Dispose() => http.Dispose();
}
