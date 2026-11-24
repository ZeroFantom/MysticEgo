namespace Models.Commands;

/// <summary>
/// Человекочитаемое имя команды для UI.
///
/// Если не задано — сканер автоматически хуманизирует CommandId:
///   audio.list → "Audio List"
///   rgb.mouse  → "Rgb Mouse"
///
/// Использование:
///   [CommandDisplayName("Список аудиоустройств")]
///   public sealed class AudioListCommand : CommandBase { ... }
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = false)]
public sealed class CommandDisplayNameAttribute : Attribute
{
    public CommandDisplayNameAttribute(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            throw new ArgumentException(
                "DisplayName is required.", nameof(displayName));

        DisplayName = displayName;
    }

    /// <summary>Отображаемое имя. Может содержать пробелы.</summary>
    public string DisplayName { get; }
}