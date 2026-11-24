using Models.Autorization.Contracts.Responses;

namespace Models.Autorization.Services;

public interface IPermissionService
{
    Task<List<PermissionDto>> ListAsync(CancellationToken ct = default);
    Task<PermissionDto?> GetAsync(int id, CancellationToken ct = default);
    Task<List<ToolDto>> ListToolsAsync(int permissionId, CancellationToken ct = default);
}