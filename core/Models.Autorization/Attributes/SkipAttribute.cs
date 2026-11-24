// ── Skip для контроллера: не создаётся Permission ───────────────────

namespace Models.Autorization.Attributes;

/// <summary>
/// Контроллер помечается как исключённый из системы прав.
/// Не создаётся ни Permission, ни Tool для его методов.
///
/// Используй для служебных контроллеров:
///   • /health
///   • /metrics
///   • /swagger
///   • внутренние технические эндпоинты
///
/// Пример:
///   [ApiController]
///   [SkipPermission]
///   public class HealthController : ControllerBase { ... }
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class SkipPermissionAttribute : Attribute { }

/// <summary>
/// Роль исключается из системы ролей.
/// Не отображается в списке ролей, не может быть назначена.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class SkipRoleAttribute : Attribute { }