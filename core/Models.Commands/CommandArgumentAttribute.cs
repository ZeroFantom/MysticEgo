namespace Models.Commands;

// Describes arguments parsed from CommandContext by commands with a fixed input contract.
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CommandArgumentAttribute(string name, string description) : Attribute
{
    public string Name { get; } = name;
    public string Description { get; } = description;
    public Type Type { get; init; } = typeof(string);
    public bool Required { get; init; } = true;
    public string? Format { get; init; }
    public string? Default { get; init; }
    public string[]? Values { get; init; }
    public string? Example { get; init; }
}
