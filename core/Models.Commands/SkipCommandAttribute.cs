// ════════════════════════════════════════════════════════════════════
//  Models.Autorization.Attributes — Skip-атрибуты
//  Всё, что помечено Skip, НЕ попадает в систему в принципе.
//  Ни в БД, ни в реестр, ни в discovery.
// ════════════════════════════════════════════════════════════════════

namespace Models.Commands;

// ── Skip для метода: не создаётся Tool ──────────────────────────────

// ── Заготовки для других систем (используются позже) ────────────────

/// <summary>
/// Команда (клиент) исключается из реестра capabilities.
/// Не отправляется на сервер при SyncCapabilities.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class SkipCommandAttribute : Attribute { }