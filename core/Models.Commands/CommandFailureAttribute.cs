namespace Models.Commands;

/// <summary>
/// Известная возможная ошибка команды.
/// Помогает LLM понять, что может пойти не так, и как реагировать.
///
/// Использование:
///   [CommandFailure("device_not_found",
///       Description = "Устройство не подключено",
///       Recovery    = "Попросите пользователя подключить устройство")]
///   public sealed class RgbMouseCommand : CommandBase { ... }
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = true)]
public sealed class CommandFailureAttribute : Attribute
{
    public CommandFailureAttribute(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException(
                "Failure code is required.", nameof(code));

        Code = code;
    }

    /// <summary>Машиночитаемый код ошибки: "device_not_found", "timeout".</summary>
    public string Code { get; }

    /// <summary>Описание ситуации для LLM. Опционально.</summary>
    public string Description { get; init; } = "";

    /// <summary>Что делать при этой ошибке. Опционально.</summary>
    public string? Recovery { get; init; }
}