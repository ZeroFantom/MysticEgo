using Models.Autorization.Contracts.Requests;
using Models.Autorization.Contracts.Responses;

namespace Models.Autorization.Services;

public interface IRoleService
{
    Task<List<RoleDto>> ListAsync(Guid callerId, bool includeHidden, CancellationToken ct = default);
    Task<RoleDto?> GetAsync(Guid callerId, int roleId, CancellationToken ct = default);
    Task<RoleDto> CreateAsync(Guid callerId, CreateRoleRequest req, CancellationToken ct = default);
    Task<RoleDto?> UpdateAsync(Guid callerId, int roleId, UpdateRoleRequest req, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid callerId, int roleId, CancellationToken ct = default);
    Task<RoleDto?> ReplacePermissionsAsync(
        Guid callerId, int roleId, IEnumerable<int> permissionIds, CancellationToken ct = default);
    Task<List<UserRoleDto>> GetUserRolesAsync(Guid userId, CancellationToken ct = default);
    Task<UserRoleDto?> AssignRoleAsync(Guid callerId, Guid userId, int roleId, CancellationToken ct = default);
    Task<bool> UnassignRoleAsync(Guid callerId, Guid userId, int roleId, CancellationToken ct = default);
}