// ════════════════════════════════════════════════════════════════════
//  Models.Commands.Client.Attributes — единый файл
//  Минимальные атрибуты для описания команд.
//
//  Ни один из них не обязателен — сканер умеет работать
//  через XML-док и имя класса. Атрибуты нужны только там,
//  где хочется явно переопределить автоматику.
//
//  Namespace: Models.Commands.Client.Attributes
// ════════════════════════════════════════════════════════════════════

namespace Models.Commands;

// ════════════════════════════════════════════════════════════════════
//  CommandDescriptionAttribute
//  Описание команды для LLM. Приоритет выше XML-дока и [Description].
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Описание команды для LLM и UI.
///
/// Если атрибут не задан — сканер берёт описание из
/// [System.ComponentModel.Description] или XML-дока &lt;summary&gt;.
///
/// Использование:
///   [CommandDescription("Список активных аудиоустройств")]
///   public sealed class AudioListCommand : CommandBase { ... }
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class CommandDescriptionAttribute : Attribute
{
    public CommandDescriptionAttribute(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                "Description is required.", nameof(description));

        Description = description;
    }

    /// <summary>Описание для LLM. Одна строка, до 500 символов.</summary>
    public string Description { get; }
}

// ════════════════════════════════════════════════════════════════════
//  CommandDisplayNameAttribute
//  Человекочитаемое имя. Если не задано — Humanize(CommandId).
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  CommandExampleAttribute
//  Пример вызова. Опционально. Можно несколько раз на команду.
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  CommandFailureAttribute
//  Известная ошибка команды. Опционально. Можно несколько раз.
// ════════════════════════════════════════════════════════════════════