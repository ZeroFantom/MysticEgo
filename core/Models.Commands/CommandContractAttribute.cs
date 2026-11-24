namespace Models.Commands;

/// <summary>Explicit stable identifier and semantic contract version for a command.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class CommandContractAttribute : Attribute
{
    public CommandContractAttribute(string id, int version = 1)
    {
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = CommandId.FromName(id).Value;
        Version = version;
    }
    public string Id { get; }
    public int Version { get; }
}
