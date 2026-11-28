using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Models.Autorization.Services;

/// <summary>
/// Telegram OIDC ID token verification. Signing keys must come from the configured, fixed
/// Telegram JWKS endpoint, not from the token or browser. Expected nonce and client ID must
/// come from a live server-owned login transaction. Does not consume state or issue sessions.
/// </summary>
public static class TelegramIdentityProof
{
    public const string Issuer = "https://oauth.telegram.org";
    public const string Jwks = Issuer + "/.well-known/jwks.json";

    public static ExternalIdentityKey? Verify(string token, string clientId, string expectedNonce,
        IEnumerable<SecurityKey> trustedKeys)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 16384 ||
            !PositiveId(clientId) || string.IsNullOrEmpty(expectedNonce) || expectedNonce.Length > 256)
            return null;
        try
        {
            var parts = token.Split('.');
            if (parts.Length != 3) return null;
            using var header = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[0]));
            using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(parts[1]));
            var claims = payload.RootElement;
            if (!UniqueObject(header.RootElement) || !UniqueObject(claims) ||
                Text(claims, "nonce") != expectedNonce ||
                Text(claims, "sub") is not { } subject ||
                !claims.TryGetProperty("iat", out var issued) || !issued.TryGetInt64(out var iat))
                return null;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (iat < now - 300 || iat > now + 30) return null;
            var result = new ExternalIdentityKey("telegram", "oidc:" + clientId, subject);
            if (!result.Valid) return null;

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false, MaximumTokenSizeInBytes = 16384 };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = Issuer,
                ValidateAudience = true, ValidAudience = clientId,
                IgnoreTrailingSlashWhenValidatingAudience = false,
                ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.FromSeconds(30),
                RequireSignedTokens = true, ValidateIssuerSigningKey = true,
                IssuerSigningKeys = trustedKeys, ValidAlgorithms = [SecurityAlgorithms.RsaSha256]
            }, out var validated);
            if (principal.Identity?.IsAuthenticated != true || validated is not JwtSecurityToken jwt ||
                jwt.Audiences.Count() != 1 || jwt.Audiences.Single() != clientId ||
                jwt.ValidTo <= DateTime.UtcNow)
                return null;
            return result;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or FormatException or JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static bool UniqueObject(JsonElement node) => node.ValueKind == JsonValueKind.Object &&
        node.EnumerateObject().Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == node.EnumerateObject().Count();
    internal static string? Text(JsonElement node, string key) =>
        node.TryGetProperty(key, out var item) && item.ValueKind == JsonValueKind.String ? item.GetString() : null;
    internal static bool PositiveId(string? value) => value is { Length: > 0 and <= 20 } &&
        ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
        number > 0 && number.ToString(CultureInfo.InvariantCulture) == value;
}
