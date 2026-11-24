namespace Models.Autorization.Attributes;

/// <summary>
/// Помечает МЕТОД контроллера как TOOL.
/// Код тула по умолчанию = {permissionCode}.{action}, в lowercase.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class ToolAttribute : Attribute
{
    public ToolAttribute() { }

    public ToolAttribute(string code)
    {
        if (!string.IsNullOrWhiteSpace(code)) Code = code;
    }

    public string? Code { get; }
    public string? DisplayName { get; init; }
    public bool Public { get; init; }
    public bool Skip { get; init; }
}