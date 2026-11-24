namespace Models.Commands;

/// <summary>
/// Метод контроллера исключается из системы прав.
/// Permission контроллера остаётся, Tool для этого метода — не создаётся.
///
/// Используй для:
///   • технических методов (warmup, ping)
///   • backward-compat эндпоинтов, которые не должны попадать в реестр
///
/// Пример:
///   [HttpGet("warmup")]
///   [SkipTool]
///   public IActionResult Warmup() => Ok();
/// </summary>
[AttributeUsage(
    AttributeTargets.Method,
    Inherited = false,
    AllowMultiple = false)]
public sealed class SkipToolAttribute : Attribute { }