using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models.Autorization.Attributes;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Gateway-facing Telegram sign-in. No provider tokens or session credentials in URLs.
/// Configuration defaults to disabled. Existing account linking is a separate feature.</summary>
[ApiController, AllowAnonymous, SkipPermission, Route("api/auth/external/login/{provider}")]
[EnableRateLimiting("external-link")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExternalLoginController(ExternalLoginService login, IConfiguration config) : ControllerBase
{
    public const string CookieName = "__Host-mystic-external-login";
    public const string CallbackPath = "/api/auth/browser/external/telegram/callback";
    public sealed record CompleteRequest(string State, string Code, string? DeviceId = null);
    private static string CookieFor(string provider) => provider == "vk" ? "__Host-mystic-external-vk-login" : CookieName;
    public static ExternalCallbackConfiguration? ReadConfiguration(IConfiguration config, string provider = "telegram")
    {
        if (provider is not ("telegram" or "vk")) return null;
        var prefix = provider == "telegram" ? "ExternalIdentity:Telegram:" : "ExternalIdentity:Vk:";
        if (!config.GetValue<bool>(prefix + "LoginEnabled")) return null;
        var id = config[prefix + "ClientId"];
        var secret = config[prefix + "ClientSecret"];
        if (!ulong.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
            number == 0 || number.ToString(System.Globalization.CultureInfo.InvariantCulture) != id ||
            provider == "telegram" && (string.IsNullOrWhiteSpace(secret) || secret.Length > 1024 || secret.Any(char.IsControl)) ||
            !Uri.TryCreate(config[prefix + "LoginRedirectUri"], UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttps || redirect.UserInfo.Length != 0 ||
            redirect.AbsolutePath != (provider == "telegram" ? CallbackPath : "/api/auth/browser/external/vk/callback") || redirect.Query.Length != 0 || redirect.Fragment.Length != 0) return null;
        return new(provider, id!, redirect, provider == "telegram" ? secret : null);
    }
    private bool OriginAllowed(ExternalCallbackConfiguration c) =>
        Request.Headers.Origin.Count == 1 &&
        string.Equals(Request.Headers.Origin.ToString(), c.RedirectUri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal) &&
        Request.Headers["X-Mystic-Browser"].ToString() == "1";
    private void PrivateResponse()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    private static CookieOptions Options() => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/",
        IsEssential = true, MaxAge = ExternalLoginService.Lifetime
    };
    private static string Binding() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [HttpGet("status")]
    public IActionResult Status(string provider)
    {
        PrivateResponse();
        return Ok(new { enabled = ReadConfiguration(config, provider) is not null });
    }

    [HttpPost("start"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Start(string provider, CancellationToken ct)
    {
        PrivateResponse();
        var configured = ReadConfiguration(config, provider);
        if (configured is null) return NotFound();
        if (!OriginAllowed(configured)) return StatusCode(403);
        await login.PurgeExpired(ct);
        var browser = Binding();
        var flow = await login.Start(configured, browser, ct);
        if (flow is null) return Conflict();
        var url = provider == "telegram" ? TelegramOAuthExchange.AuthorizationUri(configured.ClientId, configured.RedirectUri, flow) :
            VkIdentityProof.AuthorizationUri(configured.ClientId, configured.RedirectUri, flow);
        if (url is null) return Conflict();
        Response.Cookies.Append(CookieFor(provider), browser, Options());
        return Ok(new { authorizationUrl = url.AbsoluteUri });
    }

    // Only the same-origin gateway should call this POST. Never put the result in a browser redirect.
    [HttpPost("complete"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Complete(string provider, CompleteRequest input, CancellationToken ct)
    {
        PrivateResponse();
        var configured = ReadConfiguration(config, provider);
        if (configured is null) return NotFound();
        if (!OriginAllowed(configured)) return StatusCode(403);
        if (!Request.Cookies.TryGetValue(CookieFor(provider), out var browser) ||
            Request.Headers.Cookie.ToString().Split(';', StringSplitOptions.TrimEntries)
                .Count(x => x.StartsWith(CookieFor(provider) + "=", StringComparison.Ordinal)) != 1) return Unauthorized();
        var result = await login.Complete(configured, input.State, browser, input.Code, provider == "vk" ? input.DeviceId : null,
            HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString(), ct);
        Response.Cookies.Delete(CookieFor(provider), Options());
        return result is null ? Unauthorized() : Ok(result);
    }
}
