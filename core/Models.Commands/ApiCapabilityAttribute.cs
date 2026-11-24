namespace Models.Commands;

/// <summary>
/// Объявляет понятный для ИИ контракт HTTP-операции.
/// Это только описание; сервер ОБЯЗАН самостоятельно проверить JWT, права и согласие.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class ApiCapabilityAttribute : Attribute
{
    public ApiCapabilityAttribute(string id, string description, string scope, string permission = "")
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("Идентификатор и область команды обязательны.");
        Id=id;
        Description=description;
        Scope=scope;
        Permission=permission;
    }
    public string Id { get; }
    public string Description { get; }
    public string Scope { get; }
    public string Permission { get; }
    public bool RequiresConfirmation { get; set; }
}
