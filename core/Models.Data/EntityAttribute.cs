namespace Models.Data;

/// <summary>Marks a DTO as a dynamically stored, versioned entity.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityAttribute : Attribute
{
    public EntityAttribute(string id, int version = 1)
    {
        if (string.IsNullOrWhiteSpace(id) || !System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z][a-z0-9]*(\\.[a-z][a-z0-9]*)+$"))
            throw new ArgumentException("Идентификатор сущности должен содержать не менее двух сегментов в формате lower.dot.case.", nameof(id));
        if (version < 1) throw new ArgumentOutOfRangeException(nameof(version));
        Id = id;
        Version = version;
    }
    public string Id { get; }
    public int Version { get; }
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class EntityKeyAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Property)]
public sealed class EntityFieldAttribute : Attribute
{
    public bool Required { get; init; }
}
