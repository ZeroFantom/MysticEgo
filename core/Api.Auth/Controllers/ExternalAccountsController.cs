using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Security;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Experimental account-only external identity linking. Disabled by default.
/// Never issues a login session or accepts a provider identity directly from a request.</summary>
[ApiController, Authorize, SkipPermission, Route("api/auth/external")]
[EnableRateLimiting("external-link")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ExternalAccountsController(AuthDbContext db, IPasswordHasher passwords,
    IConfiguration configuration, ExternalLinkIntents links, ExternalOAuthFlows flows,
    ExternalOAuthCallbacks callbacks) : BaseApiController
{
    private const string Cookie = "__Host-mystic-external-link";
    private const string CallbackPath = "/api/auth/external/telegram/callback";
    public sealed record StartRequest(string Password);
    public sealed record CandidateRequest(string LinkCode);
    public sealed record ConfirmRequest(string LinkCode, string Provider, string Realm, string Subject, string Password);
    public sealed record UnlinkRequest(string Provider, string Realm, string Subject, string Password);

    private static string LinkCookie(string provider) => provider == "vk" ? "__Host-mystic-external-vk-link" : Cookie;
    private static string Realm(ExternalCallbackConfiguration c) => (c.Provider == "vk" ? "oauth:" : "oidc:") + c.ClientId;

    private ExternalCallbackConfiguration? Provider(string provider)
    {
        if (provider == "telegram") return Telegram();
        if (provider != "vk" || !configuration.GetValue<bool>("ExternalIdentity:Vk:Enabled")) return null;
        var id = configuration["ExternalIdentity:Vk:ClientId"];
        if (!ulong.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ||
            n == 0 || n.ToString(System.Globalization.CultureInfo.InvariantCulture) != id ||
            !Uri.TryCreate(configuration["ExternalIdentity:Vk:RedirectUri"], UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttps || redirect.UserInfo.Length != 0 ||
            redirect.AbsolutePath != "/api/auth/external/vk/callback" || redirect.Query.Length != 0 || redirect.Fragment.Length != 0) return null;
        return new("vk", id!, redirect);
    }

    private ExternalCallbackConfiguration? Telegram()
    {
        if (!configuration.GetValue<bool>("ExternalIdentity:Telegram:Enabled")) return null;
        var id = configuration["ExternalIdentity:Telegram:ClientId"];
        var secret = configuration["ExternalIdentity:Telegram:ClientSecret"];
        if (!ulong.TryParse(id, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) ||
            number == 0 || number.ToString(System.Globalization.CultureInfo.InvariantCulture) != id ||
            string.IsNullOrWhiteSpace(secret) || secret.Length > 1024 || secret.Any(char.IsControl) ||
            !Uri.TryCreate(configuration["ExternalIdentity:Telegram:RedirectUri"], UriKind.Absolute, out var redirect) ||
            redirect.Scheme != Uri.UriSchemeHttps || redirect.UserInfo.Length != 0 ||
            redirect.AbsolutePath != CallbackPath || redirect.Query.Length != 0 || redirect.Fragment.Length != 0) return null;
        return new("telegram", id!, redirect, secret);
    }

    private async Task<(Guid User, Guid Session)?> Account(CancellationToken ct, string? password = null, bool reauthenticate = false)
    {
        if (User.Identity?.IsAuthenticated != true || User.HasClaim("token_use", "device") ||
            CurrentUserId is not Guid user || user == Guid.Empty ||
            !Guid.TryParse(User.FindFirst("mystic_session")?.Value, out var session) || session == Guid.Empty)
            return null;
        var now = DateTime.UtcNow;
        var hash = await db.RefreshTokens.AsNoTracking()
            .Where(t => t.UserId == user && t.SessionId == session && t.ExpiresAt > now && t.User!.IsActive)
            .Select(t => t.User!.PasswordHash).SingleOrDefaultAsync(ct);
        if (hash is null || reauthenticate && (string.IsNullOrEmpty(password) || password.Length > 512 ||
            !passwords.Verify(password, hash))) return null;
        return (user, session);
    }

    private bool SameOrigin(ExternalCallbackConfiguration config) =>
        Request.Headers.Origin.Count == 1 &&
        string.Equals(Request.Headers.Origin.ToString(), config.RedirectUri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
    // Unlink must remain available if the provider is disabled or its credentials are removed.
    // This is a separate trusted deployment setting, falling back to the configured callback origin.
    private bool ManagementOrigin()
    {
        var configured = configuration["ExternalIdentity:ManagementOrigin"] ??
            configuration["ExternalIdentity:Telegram:RedirectUri"] ?? configuration["ExternalIdentity:Vk:RedirectUri"];
        return Uri.TryCreate(configured, UriKind.Absolute, out var uri) &&
            uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 &&
            Request.Headers.Origin.Count == 1 &&
            string.Equals(Request.Headers.Origin.ToString(), uri.GetLeftPart(UriPartial.Authority), StringComparison.Ordinal);
    }
    private void PrivateResponse()
    {
        Response.Headers.CacheControl = "no-store";
        Response.Headers["Referrer-Policy"] = "no-referrer";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
    }
    private static CookieOptions CookieSettings() => new()
    {
        Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, Path = "/",
        IsEssential = true, MaxAge = ExternalLinkIntents.Lifetime
    };
    private static string Binding() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        PrivateResponse();
        var actor = await Account(ct);
        if (actor is null) return Unauthorized();
        var rows = await db.ExternalIdentities.AsNoTracking().Where(x => x.UserId == actor.Value.User)
            .Select(x => new { x.Provider, x.Realm, x.Subject, x.LinkedUtc }).ToListAsync(ct);
        return Ok(new { telegramLinkEnabled = Telegram() is not null, vkLinkEnabled = Provider("vk") is not null, identities = rows });
    }

    [HttpPost("telegram/start"), RequestSizeLimit(4096)]
    public Task<IActionResult> Start(StartRequest input, CancellationToken ct) => StartFor("telegram", input, ct);

    [HttpPost("vk/start"), RequestSizeLimit(4096)]
    public Task<IActionResult> VkStart(StartRequest input, CancellationToken ct) => StartFor("vk", input, ct);

    private async Task<IActionResult> StartFor(string provider, StartRequest input, CancellationToken ct)
    {
        PrivateResponse();
        var config = Provider(provider);
        if (config is null) return NotFound();
        if (!SameOrigin(config)) return StatusCode(403);
        var actor = await Account(ct, input.Password, true);
        if (actor is null) return Unauthorized();
        // Bounded maintenance on this authenticated, throttled entry point; scheduled cleanup
        // and account-wide throttling remain rollout gates.
        await links.PurgeExpired(ct);
        var code = await links.Start(actor.Value.User, actor.Value.Session, provider, Realm(config), ct);
        if (code is null) return Conflict();
        var browser = Binding();
        var flow = await flows.Start(actor.Value.User, actor.Value.Session, code, browser,
            provider, config.ClientId, config.RedirectUri, ct);
        if (flow is null) return Conflict();
        var url = provider == "telegram" ? TelegramOAuthExchange.AuthorizationUri(config.ClientId, config.RedirectUri, flow) :
            VkIdentityProof.AuthorizationUri(config.ClientId, config.RedirectUri, flow);
        if (url is null) return Conflict();
        Response.Cookies.Append(LinkCookie(provider), browser, CookieSettings());
        return Ok(new { linkCode = code, authorizationUrl = url.AbsoluteUri, expiresInSeconds = 300 });
    }

    [HttpGet("telegram/callback"), AllowAnonymous]
    public Task<IActionResult> TelegramCallback(CancellationToken ct) => CallbackFor("telegram", ct);

    [HttpGet("vk/callback"), AllowAnonymous]
    public Task<IActionResult> VkCallback(CancellationToken ct) => CallbackFor("vk", ct);

    private async Task<IActionResult> CallbackFor(string provider, CancellationToken ct)
    {
        PrivateResponse();
        var config = Provider(provider);
        if (config is null) return NotFound();
        // Reject repeated parameters, provider denial and absent browser correlation.
        if (Request.Query.ContainsKey("error") || Request.Query["state"].Count != 1 ||
            Request.Query["code"].Count != 1 || provider == "vk" && Request.Query["device_id"].Count != 1 ||
            !Request.Cookies.TryGetValue(LinkCookie(provider), out var browser) ||
            Request.Headers.Cookie.ToString().Split(';', StringSplitOptions.TrimEntries)
                .Count(x => x.StartsWith(LinkCookie(provider) + "=", StringComparison.Ordinal)) != 1)
            return BadRequest();
        var ok = await callbacks.Complete(config, Request.Query["state"].ToString(), browser,
            Request.Query["code"].ToString(), provider == "vk" ? Request.Query["device_id"].ToString() : null, ct);
        if (!ok) return BadRequest();
        Response.Cookies.Delete(LinkCookie(provider), CookieSettings());
        // No code, state, subject, token, HTML script or dynamic redirect in the response.
        return Content("External account verified. Return to the original Mystic Ego account screen to confirm the link.", "text/plain");
    }

    [HttpPost("telegram/candidate"), RequestSizeLimit(4096)]
    public Task<IActionResult> Candidate(CandidateRequest input, CancellationToken ct) => CandidateFor("telegram", input, ct);

    [HttpPost("vk/candidate"), RequestSizeLimit(4096)]
    public Task<IActionResult> VkCandidate(CandidateRequest input, CancellationToken ct) => CandidateFor("vk", input, ct);

    private async Task<IActionResult> CandidateFor(string provider, CandidateRequest input, CancellationToken ct)
    {
        PrivateResponse();
        var config = Provider(provider);
        if (config is null) return NotFound();
        if (!SameOrigin(config)) return StatusCode(403);
        var actor = await Account(ct);
        if (actor is null) return Unauthorized();
        var proof = await links.Candidate(actor.Value.User, actor.Value.Session, input.LinkCode, ct);
        if (proof is null || proof.Provider != provider || proof.Realm != Realm(config))
            return NotFound();
        return Ok(proof);
    }

    [HttpPost("telegram/confirm"), RequestSizeLimit(8192)]
    public Task<IActionResult> Confirm(ConfirmRequest input, CancellationToken ct) => ConfirmFor("telegram", input, ct);

    [HttpPost("vk/confirm"), RequestSizeLimit(8192)]
    public Task<IActionResult> VkConfirm(ConfirmRequest input, CancellationToken ct) => ConfirmFor("vk", input, ct);

    private async Task<IActionResult> ConfirmFor(string provider, ConfirmRequest input, CancellationToken ct)
    {
        PrivateResponse();
        var config = Provider(provider);
        if (config is null) return NotFound();
        if (!SameOrigin(config)) return StatusCode(403);
        var actor = await Account(ct, input.Password, true);
        if (actor is null) return Unauthorized();
        if (input.Provider != provider || input.Realm != Realm(config))
            return Conflict();
        var result = await links.Confirm(actor.Value.User, actor.Value.Session, input.LinkCode,
            new(input.Provider, input.Realm, input.Subject), ct);
        return result == ExternalIdentityLinkResult.Unavailable ? Conflict() : NoContent();
    }

    [HttpPost("unlink"), RequestSizeLimit(8192)]
    public async Task<IActionResult> Unlink(UnlinkRequest input, CancellationToken ct)
    {
        PrivateResponse();
        if (!ManagementOrigin()) return StatusCode(403);
        var actor = await Account(ct);
        if (actor is null) return Unauthorized();
        var result = await new ExternalIdentityRevocation(db, passwords, TimeProvider.System).Unlink(
            actor.Value.User, actor.Value.Session, new(input.Provider, input.Realm, input.Subject), input.Password, ct);
        if (result is null) return Conflict();
        Response.Cookies.Delete(Cookie, CookieSettings());
        Response.Cookies.Delete(LinkCookie("vk"), CookieSettings());
        return Ok(new { reauthenticate = true, revokedSessions = result.RevokedSessions });
    }

    [HttpGet("audit")]
    public async Task<IActionResult> Audit(CancellationToken ct)
    {
        PrivateResponse();
        var actor = await Account(ct);
        if (actor is null) return Unauthorized();
        var events = await db.ExternalIdentityAudits.AsNoTracking()
            .Where(x => x.UserId == actor.Value.User).OrderByDescending(x => x.OccurredUtc)
            .ThenByDescending(x => x.Id).Take(100)
            .Select(x => new { x.Provider, x.Realm, x.Action, x.OccurredUtc, x.RevokedSessions }).ToListAsync(ct);
        return Ok(events);
    }
}
