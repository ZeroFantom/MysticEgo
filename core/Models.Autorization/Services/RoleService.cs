using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Autorization.Contracts.Requests;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Internal;

namespace Models.Autorization.Services;

public sealed class RoleService : IRoleService
{
    private readonly AuthDbContext _db;
    private readonly IAccessResolver _access;
    private readonly ILogger<RoleService> _log;

    public RoleService(AuthDbContext db, IAccessResolver access, ILogger<RoleService> log)
    {
        _db = db;
        _access = access;
        _log = log;
    }

    public async Task<List<RoleDto>> ListAsync(
        Guid callerId, bool includeHidden, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var q = _db.Roles.AsNoTracking()
            .Include(r => r.RolePermissions).ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.Tools)
            .AsQueryable();

        if (!includeHidden) q = q.Where(r => !r.IsHidden);

        if (!caller.HasBypass)
            q = q.Where(r => !r.IsSystem || !r.IsHidden);

        var list = await q
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.DisplayName)
            .ToListAsync(ct);

        return list.Select(ToDto).ToList();
    }

    public async Task<RoleDto?> GetAsync(Guid callerId, int roleId, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var r = await _db.Roles.AsNoTracking()
            .Include(x => x.RolePermissions).ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.Tools)
            .FirstOrDefaultAsync(x => x.Id == roleId, ct);

        if (r is null) return null;

        if (r.IsHidden && r.IsSystem && !caller.HasBypass)
            return null;

        return ToDto(r);
    }

    public async Task<RoleDto> CreateAsync(
        Guid callerId, CreateRoleRequest req, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var code = (req.Code ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(code))
            throw new ValidationException("Код роли обязателен.");

        if (await _db.Roles.AnyAsync(r => r.Code == code, ct))
            throw new ValidationException($"Роль '{code}' уже существует.");

        if (req.Priority >= caller.MaxPriority && !caller.HasBypass)
            throw new ForbiddenException(
                "Нельзя создать роль с приоритетом ≥ своего.");

        var role = new Role
        {
            Code = code,
            DisplayName = req.DisplayName?.Trim() ?? code,
            Description = req.Description?.Trim(),
            IsSystem = false,
            IsHidden = req.IsHidden,
            Priority = req.Priority,
            CreatedAt = DateTime.UtcNow,
            CreatedByUserId = callerId,
        };

        _db.Roles.Add(role);
        await _db.SaveChangesAsync(ct);

        if (req.PermissionIds is { Count: > 0 })
            await ReplacePermissionsAsync(callerId, role.Id, req.PermissionIds, ct);

        _access.Invalidate();

        _log.LogInformation("Role created: {Code} by {CallerId}", role.Code, callerId);

        return (await GetAsync(callerId, role.Id, ct))!;
    }

    public async Task<RoleDto?> UpdateAsync(
        Guid callerId, int roleId, UpdateRoleRequest req, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null) return null;

        if (role.IsSystem && !caller.HasBypass)
            throw new ForbiddenException("Системную роль нельзя изменять.");

        if (!caller.HasBypass && role.Priority >= caller.MaxPriority)
            throw new ForbiddenException("Нельзя изменять роль с приоритетом ≥ своего.");

        if (req.DisplayName is not null) role.DisplayName = req.DisplayName.Trim();
        if (req.Description is not null) role.Description = req.Description.Trim();
        if (req.IsHidden is not null) role.IsHidden = req.IsHidden.Value;

        if (req.Priority is not null)
        {
            if (!caller.HasBypass && req.Priority.Value >= caller.MaxPriority)
                throw new ForbiddenException("Нельзя установить приоритет ≥ своего.");
            role.Priority = req.Priority.Value;
        }

        await _db.SaveChangesAsync(ct);
        _access.Invalidate();

        return await GetAsync(callerId, roleId, ct);
    }

    public async Task<bool> DeleteAsync(Guid callerId, int roleId, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null) return false;

        if (role.IsSystem)
            throw new ForbiddenException("Системную роль нельзя удалить.");

        if (!caller.HasBypass && role.Priority >= caller.MaxPriority)
            throw new ForbiddenException("Нельзя удалить роль с приоритетом ≥ своего.");

        _db.Roles.Remove(role);
        await _db.SaveChangesAsync(ct);
        _access.Invalidate();

        _log.LogInformation("Role deleted: {Code} by {CallerId}", role.Code, callerId);
        return true;
    }

    public async Task<RoleDto?> ReplacePermissionsAsync(
        Guid callerId, int roleId, IEnumerable<int> permissionIds, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var role = await _db.Roles
            .Include(r => r.RolePermissions)
            .FirstOrDefaultAsync(r => r.Id == roleId, ct);

        if (role is null) return null;

        if (role.IsSystem && !caller.HasBypass)
            throw new ForbiddenException("Права системной роли нельзя менять.");

        if (!caller.HasBypass && role.Priority >= caller.MaxPriority)
            throw new ForbiddenException("Нельзя менять роль с приоритетом ≥ своего.");

        var ids = permissionIds.Distinct().ToHashSet();

        var perms = await _db.Permissions
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);

        if (perms.Count != ids.Count)
            throw new ValidationException("Некоторые права не найдены.");

        if (!caller.HasBypass)
        {
            foreach (var p in perms)
            {
                if (!caller.PermissionIds.Contains(p.Id))
                    throw new ForbiddenException(
                        $"Нельзя выдать право '{p.Code}', которого у вас нет.");
                if (p.IsSystem)
                    throw new ForbiddenException(
                        $"Системное право '{p.Code}' можно выдать только с bypass.");
            }
        }

        var valid = perms.Select(p => p.Id).ToHashSet();
        var current = role.RolePermissions.Select(rp => rp.PermissionId).ToHashSet();

        var toRemove = role.RolePermissions
            .Where(rp => !valid.Contains(rp.PermissionId))
            .ToList();
        foreach (var rp in toRemove) _db.RolePermissions.Remove(rp);

        foreach (var pid in valid.Except(current))
            _db.RolePermissions.Add(new RolePermission
            {
                RoleId = roleId,
                PermissionId = pid,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = callerId,
            });

        await _db.SaveChangesAsync(ct);
        _access.Invalidate();

        return await GetAsync(callerId, roleId, ct);
    }

    public async Task<List<UserRoleDto>> GetUserRolesAsync(
        Guid userId, CancellationToken ct)
    {
        return await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Include(ur => ur.Role)
            .OrderByDescending(ur => ur.Role.Priority)
            .Select(ur => new UserRoleDto
            {
                RoleId = ur.RoleId,
                Code = ur.Role.Code,
                DisplayName = ur.Role.DisplayName,
                IsPrimary = ur.IsPrimary,
                AssignedAt = ur.AssignedAt,
            })
            .ToListAsync(ct);
    }

    public async Task<UserRoleDto?> AssignRoleAsync(
        Guid callerId, Guid userId, int roleId, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return null;

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, ct);
        if (role is null) return null;

        if (role.IsSystem && !caller.HasBypass)
            throw new ForbiddenException("Системную роль нельзя назначать.");

        if (!caller.HasBypass && role.Priority >= caller.MaxPriority)
            throw new ForbiddenException(
                "Нельзя назначить роль с приоритетом ≥ своего.");

        var targetAccess = await _access.GetUserAccessAsync(userId, ct);
        if (!caller.HasBypass && targetAccess.MaxPriority >= caller.MaxPriority)
            throw new ForbiddenException(
                "Нельзя управлять ролью пользователя выше вас.");

        var existing = await _db.UserRoles
            .FirstOrDefaultAsync(ur => ur.UserId == userId && ur.RoleId == roleId, ct);

        if (existing is null)
        {
            existing = new UserRole
            {
                UserId = userId,
                RoleId = roleId,
                AssignedAt = DateTime.UtcNow,
                AssignedByUserId = callerId,
            };
            _db.UserRoles.Add(existing);
            await _db.SaveChangesAsync(ct);
            _access.InvalidateUser(userId);
        }

        return new UserRoleDto
        {
            RoleId = role.Id,
            Code = role.Code,
            DisplayName = role.DisplayName,
            IsPrimary = existing.IsPrimary,
            AssignedAt = existing.AssignedAt,
        };
    }

    public async Task<bool> UnassignRoleAsync(
        Guid callerId, Guid userId, int roleId, CancellationToken ct)
    {
        var caller = await _access.GetUserAccessAsync(callerId, ct);

        var ur = await _db.UserRoles
            .Include(x => x.Role)
            .FirstOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId, ct);

        if (ur is null) return false;

        if (ur.IsPrimary)
            throw new ValidationException("Нельзя снять основную роль пользователя.");

        if (ur.Role.IsSystem && !caller.HasBypass)
            throw new ForbiddenException("Нельзя снять системную роль.");

        if (!caller.HasBypass && ur.Role.Priority >= caller.MaxPriority)
            throw new ForbiddenException(
                "Нельзя снять роль с приоритетом ≥ своего.");

        _db.UserRoles.Remove(ur);
        await _db.SaveChangesAsync(ct);
        _access.InvalidateUser(userId);
        return true;
    }

    private static RoleDto ToDto(Role r) => new()
    {
        Id = r.Id,
        Code = r.Code,
        DisplayName = r.DisplayName,
        Description = r.Description,
        IsSystem = r.IsSystem,
        IsHidden = r.IsHidden,
        Priority = r.Priority,
        CreatedAt = r.CreatedAt,
        Permissions = r.RolePermissions
            .Select(rp => new PermissionDto
            {
                Id = rp.Permission.Id,
                Code = rp.Permission.Code,
                DisplayName = rp.Permission.DisplayName,
                Description = rp.Permission.Description,
                Category = rp.Permission.Category,
                SortOrder = rp.Permission.SortOrder,
                IsSystem = rp.Permission.IsSystem,
                ControllerName = rp.Permission.ControllerName,
                Tools = rp.Permission.Tools
                    .Select(t => new ToolDto
                    {
                        Id = t.Id,
                        Code = t.Code,
                        DisplayName = t.DisplayName,
                        Description = t.Description,
                        HttpMethod = t.HttpMethod,
                        PathTemplate = t.PathTemplate,
                        PermissionId = t.PermissionId,
                        IsEnabled = t.IsEnabled,
                        IsPublic = t.IsPublic,
                    })
                    .OrderBy(t => t.Code)
                    .ToList(),
            })
            .OrderBy(p => p.Category)
            .ThenBy(p => p.SortOrder)
            .ThenBy(p => p.Code)
            .ToList(),
    };
}