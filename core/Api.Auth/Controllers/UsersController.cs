using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Autorization.Contracts.Requests;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>
/// Управление пользователями: создание, редактирование профиля,
/// активация, смена паролей.
/// </summary>
[ApiController]
[Route("api/users")]
[Authorize]
[Produces("application/json")]
public sealed class UsersController : BaseApiController
{
    private readonly IUserService _users;

    public UsersController(IUserService users) => _users = users;

    /// <summary>
    /// Список пользователей, доступных вызывающему.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        return Ok(await _users.ListAsync(uid, ct));
    }

    /// <summary>
    /// Детали пользователя по Id.
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var dto = await _users.GetAsync(id, ct);
        if (dto is null)
            return ApiError(404, "user_not_found", $"Пользователь {id} не найден.");

        return Ok(dto);
    }

    /// <summary>
    /// Создать нового пользователя.
    /// Может назначать только те роли, чей приоритет ниже собственного.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateUserRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        return Ok(await _users.CreateAsync(uid, req, ct));
    }

    /// <summary>
    /// Обновить профиль пользователя.
    /// </summary>
    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(
        Guid id, [FromBody] UpdateUserRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var dto = await _users.UpdateAsync(uid, id, req, ct);
        if (dto is null)
            return ApiError(404, "user_not_found", $"Пользователь {id} не найден.");

        return Ok(dto);
    }

    /// <summary>
    /// Включить или выключить пользователя.
    /// </summary>
    [HttpPatch("{id:guid}/active")]
    public async Task<IActionResult> SetActive(
        Guid id, [FromBody] SetUserActiveRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var ok = await _users.SetActiveAsync(uid, id, req.IsActive, ct);
        if (!ok)
            return ApiError(404, "user_not_found", $"Пользователь {id} не найден.");

        return Ok(new { success = true });
    }

    /// <summary>
    /// Сбросить пароль другого пользователя.
    /// </summary>
    [HttpPost("{id:guid}/password")]
    public async Task<IActionResult> ChangePassword(
        Guid id, [FromBody] AdminChangePasswordRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var ok = await _users.ChangePasswordAsync(uid, id, req.NewPassword, ct);
        if (!ok)
            return ApiError(404, "user_not_found", $"Пользователь {id} не найден.");

        return Ok(new { success = true });
    }

    /// <summary>
    /// Сменить свой пароль.
    /// </summary>
    [HttpPost("me/password")]
    public async Task<IActionResult> ChangePasswordSelf(
        [FromBody] ChangePasswordRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var ok = await _users.ChangePasswordSelfAsync(
            uid, req.CurrentPassword, req.NewPassword, ct);

        if (!ok)
            return ApiError(404, "user_not_found", "Пользователь не найден.");

        return Ok(new { success = true });
    }
}