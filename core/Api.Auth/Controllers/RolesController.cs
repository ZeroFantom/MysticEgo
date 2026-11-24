using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Autorization.Contracts.Requests;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>
/// Роли и связи роль↔право↔пользователь.
/// Создание, обновление и назначение ролей.
/// </summary>
[ApiController]
[Route("api/roles")]
[Authorize]
[Produces("application/json")]
public sealed class RolesController : BaseApiController
{
    private readonly IRoleService _roles;

    public RolesController(IRoleService roles) => _roles = roles;

    /// <summary>
    /// Список ролей. Скрытые системные роли видны только bypass-юзерам.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] bool includeHidden, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        return Ok(await _roles.ListAsync(uid, includeHidden, ct));
    }

    /// <summary>
    /// Роль по Id.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var dto = await _roles.GetAsync(uid, id, ct);
        if (dto is null)
            return ApiError(404, "role_not_found", $"Роль {id} не найдена.");

        return Ok(dto);
    }

    /// <summary>
    /// Создать роль. Приоритет должен быть ниже собственного.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateRoleRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var dto = await _roles.CreateAsync(uid, req, ct);
        return CreatedAtAction(nameof(Get), new { id = dto.Id }, dto);
    }

    /// <summary>
    /// Обновить роль. Только ту, что ниже своей.
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id, [FromBody] UpdateRoleRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var dto = await _roles.UpdateAsync(uid, id, req, ct);
        if (dto is null)
            return ApiError(404, "role_not_found", $"Роль {id} не найдена.");

        return Ok(dto);
    }

    /// <summary>
    /// Удалить роль. Системные защищены.
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var ok = await _roles.DeleteAsync(uid, id, ct);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    /// <summary>
    /// Полностью заменить набор прав роли.
    /// Можно выдать только те права, которыми владеешь сам.
    /// </summary>
    [HttpPut("{id:int}/permissions")]
    public async Task<IActionResult> ReplacePermissions(
        int id, [FromBody] ReplaceRolePermissionsRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var dto = await _roles.ReplacePermissionsAsync(
            uid, id, req.PermissionIds, ct);

        if (dto is null)
            return ApiError(404, "role_not_found", $"Роль {id} не найдена.");

        return Ok(dto);
    }

    // ── User ↔ Role ──────────────────────────────────────

    /// <summary>
    /// Роли пользователя.
    /// </summary>
    [HttpGet("/api/users/{userId:guid}/roles")]
    public async Task<IActionResult> GetUserRoles(Guid userId, CancellationToken ct)
        => Ok(await _roles.GetUserRolesAsync(userId, ct));

    /// <summary>
    /// Назначить роль пользователю.
    /// </summary>
    [HttpPost("/api/users/{userId:guid}/roles")]
    public async Task<IActionResult> AssignRole(
        Guid userId, [FromBody] AssignRoleRequest req, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var dto = await _roles.AssignRoleAsync(uid, userId, req.RoleId, ct);
        if (dto is null)
            return ApiError(404, "not_found", "Пользователь или роль не найдены.");

        return Ok(dto);
    }

    /// <summary>
    /// Снять роль с пользователя (кроме основной).
    /// </summary>
    [HttpDelete("/api/users/{userId:guid}/roles/{roleId:int}")]
    public async Task<IActionResult> UnassignRole(
        Guid userId, int roleId, CancellationToken ct)
    {
        if (CurrentUserId is not Guid uid)
            return ApiError(401, "unauthorized", "Не авторизован.");

        var ok = await _roles.UnassignRoleAsync(uid, userId, roleId, ct);
        return ok ? Ok(new { success = true }) : NotFound();
    }
}