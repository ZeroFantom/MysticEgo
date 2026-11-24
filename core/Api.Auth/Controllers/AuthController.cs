using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models.Autorization.Contracts.Requests;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>
/// Аутентификация: вход, выход, обновление токенов.
/// Управление сессиями пользователя.
/// </summary>
[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public sealed class AuthController : BaseApiController
{
    private readonly IAuthService _auth;
    private readonly IAccessResolver _access;
    private readonly ILogger<AuthController> _log;

    public AuthController(
        IAuthService auth,
        IAccessResolver access,
        ILogger<AuthController> log)
    {
        _auth = auth;
        _access = access;
        _log = log;
    }

    /// <summary>
    /// Вход по логину и паролю.
    /// Возвращает access-токен (JWT, 15 мин) и refresh-токен (opaque, 7 дней).
    /// </summary>
    [HttpPost("login")]
    [EnableRateLimiting("auth-sensitive")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest req, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _auth.LoginAsync(req.Login, req.Password, ip, ua, ct);

        if (result is null)
        {
            _log.LogWarning("Login failed: {Login}", req.Login);
            return ApiError(401, "invalid_credentials", "Неверный логин или пароль.");
        }

        return Ok(result);
    }

    /// <summary>
    /// Обновить access-токен по refresh-токену.
    /// Refresh одноразовый: после использования выдаётся новая пара.
    /// </summary>
    [HttpPost("refresh")]
    [EnableRateLimiting("auth-sensitive")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest req, CancellationToken ct)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var ua = Request.Headers.UserAgent.ToString();

        var result = await _auth.RefreshAsync(req.RefreshToken, ip, ua, ct);

        if (result is null)
            return ApiError(401, "invalid_refresh_token",
                "Refresh-токен недействителен.");

        return Ok(result);
    }

    /// <summary>
    /// Выход. Удаляет refresh-токен. Идемпотентно.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(
        [FromBody] RefreshRequest req, CancellationToken ct)
    {
        await _auth.LogoutAsync(req.RefreshToken, ct);
        return Ok(new SimpleResult { Success = true });
    }

    /// <summary>
    /// Отозвать все сессии текущего пользователя.
    /// </summary>
    [HttpPost("revoke-all")]
    [Authorize]
    public async Task<IActionResult> RevokeAll(CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var count = await _auth.RevokeAllAsync(uid, ct);
        return Ok(new { success = true, revoked = count });
    }

    /// <summary>
    /// Информация о текущем пользователе.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        return Ok(new MeDto
        {
            Id = uid,
            Login = CurrentLogin ?? "",
        });
    }

    /// <summary>
    /// Полный снимок доступа: роли, права, инструменты, bypass, приоритет.
    /// Фронт использует, чтобы скрывать/показывать элементы UI.
    /// </summary>
    [HttpGet("my-access")]
    [Authorize]
    public async Task<IActionResult> MyAccess(CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var snap = await _access.GetUserAccessAsync(uid, ct);

        return Ok(new MyAccessDto
        {
            UserId = snap.UserId,
            Login = CurrentLogin ?? "",
            HasBypass = snap.HasBypass,
            MaxPriority = snap.MaxPriority,
            Roles = snap.Roles.ToList(),
            Permissions = snap.PermissionIds.Select(id => id.ToString()).ToList(),
            Tools = snap.ToolIds.Select(id => id.ToString()).ToList(),
        });
    }
}