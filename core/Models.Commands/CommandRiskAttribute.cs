namespace Models.Commands;

/// <summary>Декларативный риск операции. Метаданные отображаются в UI и MCP, но НЕ заменяют проверку полномочий сервером.</summary>
public enum CommandRisk { ReadOnly, ChangesState, Privileged }

[AttributeUsage(AttributeTargets.Class,AllowMultiple=false,Inherited=false)]
public sealed class CommandRiskAttribute : Attribute
{
    public CommandRiskAttribute(CommandRisk risk, bool requiresHumanApproval=false, string? permission=null)
    {
        Risk=risk;
        RequiresHumanApproval=requiresHumanApproval;
        Permission=permission;
    }
    public CommandRisk Risk {get;}
    public bool RequiresHumanApproval {get;}
    public string? Permission {get;}
}
