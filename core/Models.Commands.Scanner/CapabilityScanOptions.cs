// ════════════════════════════════════════════════════════════════════
//  Models.Commands.Scanner — полный сканер capabilities
//  Namespace: Models.Commands.Scanner
// ════════════════════════════════════════════════════════════════════

namespace Models.Commands.Scanner;

// ════════════════════════════════════════════════════════════════════
//  ICapabilityScanner
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  CapabilityScanOptions
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Настройки сканера. Всё с разумными дефолтами.
/// </summary>
public sealed class CapabilityScanOptions
{
    /// <summary>Требовать, чтобы имя класса заканчивалось на "Command".</summary>
    public bool RequireCommandSuffix { get; init; } = true;

    /// <summary>Типы-исключения по имени.</summary>
    public IReadOnlySet<string> ExcludedTypeNames { get; init; } = new HashSet<string>(
        new[] { "UnknownCommand", "RejectedCommand" },
        StringComparer.Ordinal);

    /// <summary>Дополнительный фильтр. null — не применять.</summary>
    public Func<Type, bool>? AdditionalFilter { get; init; }

    /// <summary>Читать &lt;summary&gt; из XML-документа сборки.</summary>
    public bool ReadXmlDoc { get; init; } = true;

    /// <summary>Строгий режим: если нет ни [Description], ни XML-дока — исключение.</summary>
    public bool RequireDescription { get; init; }

    /// <summary>Кэшировать результат по сборке.</summary>
    public bool CacheResults { get; init; } = true;

    /// <summary>Логировать Debug-сообщения по каждому типу.</summary>
    public bool VerboseDiagnostics { get; init; } = true;

    public static CapabilityScanOptions Default { get; } = new();
}

// ════════════════════════════════════════════════════════════════════
//  CapabilityScanner
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  CapabilityHeuristics — эвристики
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  XmlDocSummaryReader — читает <summary> и <param>
// ════════════════════════════════════════════════════════════════════