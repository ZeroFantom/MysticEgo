namespace Models.Autorization.Services;

public sealed record UserAccessSnapshot(
    Guid UserId,
    bool HasBypass,
    int MaxPriority,
    IReadOnlyList<string> Roles,
    IReadOnlySet<int> PermissionIds,
    IReadOnlySet<int> ToolIds);