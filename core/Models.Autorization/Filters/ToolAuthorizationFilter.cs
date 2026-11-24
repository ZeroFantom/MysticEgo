using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using Models.Autorization.Attributes;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Services;
using Models.Commands;

namespace Models.Autorization.Filters;

/// <summary>
/// Глобальный фильтр авторизации.
///
/// Что делает:
///   • [AllowAnonymous]                     → пропускает (публичный)
///   • [SkipPermission] на контроллере      → пропускает (вне системы)
///   • [SkipTool] на методе                 → пропускает (вне системы)
///   • контроллер без [ApiController]       → пропускает (вне системы)
///   • Иначе строит ToolId по той же логике, что discovery,
///     и проверяет доступ пользователя.
///
/// Логика построения ToolId совпадает 1:1 с ToolDiscoveryService:
///   PermissionId.FromController(ControllerName) + ToolId.From(..., ActionName)
/// </summary>
public sealed class ToolAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly IAccessResolver _access;
    private readonly ILogger<ToolAuthorizationFilter> _log;

    public ToolAuthorizationFilter(
        IAccessResolver access,
        ILogger<ToolAuthorizationFilter> log)
    {
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // ── 1. Явные skip-маркеры ──────────────────────
        if (HasMetadata<AllowAnonymousAttribute>(context))
            return;

        if (HasMetadata<SkipPermissionAttribute>(context))
            return;

        if (HasMetadata<SkipToolAttribute>(context))
            return;

        // ── 2. Нужен авторизованный пользователь ───────
        var http = context.HttpContext;
        var user = http.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            context.Result = Error(context, StatusCodes.Status401Unauthorized,
                "unauthorized", "Требуется авторизация.");
            return;
        }

        if (context.ActionDescriptor is not ControllerActionDescriptor action)
            return;

        // ── 3. Контроллер должен быть частью системы ───
        // Критерий тот же, что и в discovery: [ApiController].
        var isApiController = action.ControllerTypeInfo
            .GetCustomAttributes(typeof(ApiControllerAttribute), inherit: true)
            .Any();

        if (!isApiController)
            return;

        // ── 4. Строим ToolId ТОЧНО как discovery ───────
        var permissionId = PermissionId.FromController(action.ControllerName);
        var toolId = ToolId.From(permissionId, action.ActionName);
        var toolCode = toolId.Value;

        // ── 5. Ищем Tool в реестре ─────────────────────
        var tool = await _access.GetToolByKeyAsync(toolCode, http.RequestAborted);

        if (tool is null)
        {
            // Не зарегистрирован — исключение только для bypass-пользователей.
            var uid = GetUserId(user);
            if (uid is Guid userId)
            {
                var snap = await _access.GetUserAccessAsync(userId, http.RequestAborted);
                if (snap.HasBypass) return;
            }

            _log.LogWarning(
                "Tool not registered: '{Code}' (controller={Controller}, action={Action})",
                toolCode, action.ControllerName, action.ActionName);

            context.Result = Error(context, StatusCodes.Status403Forbidden,
                "tool_not_registered",
                $"Инструмент '{toolCode}' не зарегистрирован в системе.");
            return;
        }

        // ── 6. Отключённый tool ────────────────────────
        if (!tool.IsEnabled)
        {
            context.Result = Error(context, StatusCodes.Status503ServiceUnavailable,
                "tool_disabled", "Инструмент временно отключён.");
            return;
        }

        // ── 7. Публичный tool — пропускаем ─────────────
        if (tool.IsPublic)
            return;

        // ── 8. Проверка прав ───────────────────────────
        var finalUserId = GetUserId(user);
        if (finalUserId is null)
        {
            context.Result = Error(context, StatusCodes.Status401Unauthorized,
                "unauthorized", "Невалидный токен.");
            return;
        }

        var access = await _access.GetUserAccessAsync(finalUserId.Value, http.RequestAborted);

        if (access.HasBypass) return;

        if (!access.ToolIds.Contains(tool.Id))
        {
            _log.LogDebug(
                "Tool forbidden: user={UserId}, tool={Code}",
                finalUserId, toolCode);

            context.Result = Error(context, StatusCodes.Status403Forbidden,
                "forbidden", "Недостаточно прав.");
        }
    }

    // ── Helpers ────────────────────────────────────────

    private static bool HasMetadata<T>(AuthorizationFilterContext ctx)
        where T : Attribute
        => ctx.ActionDescriptor.EndpointMetadata.Any(m => m is T);

    private static Guid? GetUserId(ClaimsPrincipal user)
    {
        var raw = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out var id) ? id : null;
    }

    private static IActionResult Error(
        AuthorizationFilterContext ctx, int status, string code, string message)
        => new ObjectResult(new ApiErrorDto
        {
            Code = code,
            Message = message,
            TraceId = ctx.HttpContext.TraceIdentifier,
        })
        { StatusCode = status };
}