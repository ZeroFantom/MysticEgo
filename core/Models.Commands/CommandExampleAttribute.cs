namespace Models.Commands;

/// <summary>
/// Пример вызова команды. Помогает LLM понять, как её использовать.
///
/// Можно повесить несколько:
///   [CommandExample("rgb.mouse red", "Красный")]
///   [CommandExample("rgb.mouse #00FF00", "Зелёный в HEX")]
///   public sealed class RgbMouseCommand : CommandBase { ... }
/// </summary>
[AttributeUsage(
    AttributeTargets.Class,
    Inherited = false,
    AllowMultiple = true)]
public sealed class CommandExampleAttribute : Attribute
{
    public CommandExampleAttribute(string invocation)
    {
        if (string.IsNullOrWhiteSpace(invocation))
            throw new ArgumentException(
                "Invocation is required.", nameof(invocation));

        Invocation = invocation;
    }

    /// <summary>Полная строка вызова: "rgb.mouse red".</summary>
    public string Invocation { get; }

    /// <summary>Пояснение к примеру. Опционально.</summary>
    public string Description { get; init; } = "";
}