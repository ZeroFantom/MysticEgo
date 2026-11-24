namespace Models.Autorization.Services;

public interface IAccessResolver
{
    Task<ToolAccessInfo?> GetToolByKeyAsync(string key, CancellationToken ct = default);
    Task<ToolAccessInfo?> GetToolByIdAsync(int id, CancellationToken ct = default);
    Task<UserAccessSnapshot> GetUserAccessAsync(Guid userId, CancellationToken ct = default);
    void Invalidate();
    void InvalidateUser(Guid userId);
}