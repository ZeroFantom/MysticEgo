using System.Reflection;

namespace Models.Commands;

/// <summary>Single source of truth for command ID and skip semantics (registry and scanner).</summary>
public static class CommandMetadata
{
    public static bool IsCommand(Type type) =>
        type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters &&
        typeof(ICommand).IsAssignableFrom(type) &&
        !Attribute.IsDefined(type, typeof(SkipCommandAttribute), false) &&
        (type.Name.EndsWith("Command", StringComparison.Ordinal) ||
         type.IsDefined(typeof(CommandContractAttribute), false));

    public static CommandId GetId(Type type)
    {
        var contract = type.GetCustomAttribute<CommandContractAttribute>();
        return contract is null
            ? CommandId.FromName(IdentifierConvention.FromTypeName(type.Name, "Command"))
            : CommandId.FromName(contract.Id);
    }

    public static int GetVersion(Type type) =>
        type.GetCustomAttribute<CommandContractAttribute>()?.Version ?? 1;
}
