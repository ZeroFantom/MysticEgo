using System.Reflection;
using System.Text.Json;

namespace Models.Data;

public sealed record FieldDescriptor(string Name, string Type, bool Required, bool Key);
public sealed record EntityDescriptor(string Id, int Version, Type ClrType, string KeyName, IReadOnlyList<FieldDescriptor> Fields)
{
    public string SchemaJson => JsonSerializer.Serialize(new { id = Id, version = Version, fields = Fields });
}

/// <summary>Immutable descriptions built from entity attributes; rejects ambiguous models.</summary>
public static class EntityMetadata
{
    public static EntityDescriptor Describe<T>() where T : class => Describe(typeof(T));

    public static EntityDescriptor Describe(Type type)
    {
        var entity = type.GetCustomAttribute<EntityAttribute>()
            ?? throw new InvalidOperationException($"{type.FullName} has no [Entity].");
        if (!type.IsClass || type.IsAbstract || type.ContainsGenericParameters)
            throw new InvalidOperationException("Сущности должны быть конкретными классами.");
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetMethod is not null && p.SetMethod is not null &&
                        p.GetIndexParameters().Length == 0)
            .OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        var keys = props.Where(p => p.IsDefined(typeof(EntityKeyAttribute), false)).ToArray();
        if (keys.Length != 1 || keys[0].PropertyType != typeof(Guid))
            throw new InvalidOperationException($"{type.Name} needs exactly one Guid [EntityKey] property.");
        var fields = props.Select(p => new FieldDescriptor(p.Name,
            Nullable.GetUnderlyingType(p.PropertyType)?.FullName ?? p.PropertyType.FullName ?? p.PropertyType.Name,
            p.GetCustomAttribute<EntityFieldAttribute>()?.Required ?? false,
            p == keys[0])).ToArray();
        return new EntityDescriptor(entity.Id, entity.Version, type, keys[0].Name, fields);
    }
}
