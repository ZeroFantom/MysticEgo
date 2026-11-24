namespace Models.Autorization.Services;

public sealed record ToolAccessInfo(
    int Id, string Code, bool IsEnabled, bool IsPublic, int PermissionId);