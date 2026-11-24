using Microsoft.EntityFrameworkCore;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;

namespace Models.Autorization.Services;

public sealed class PermissionService : IPermissionService
{
    private readonly AuthDbContext _db;

    public PermissionService(AuthDbContext db) => _db = db;

    public async Task<List<PermissionDto>> ListAsync(CancellationToken ct)
    {
        var perms = await _db.Permissions.AsNoTracking()
            .Include(p => p.Tools)
            .OrderBy(p => p.Category).ThenBy(p => p.SortOrder).ThenBy(p => p.Code)
            .ToListAsync(ct);

        return perms.Select(ToDto).ToList();
    }

    public async Task<PermissionDto?> GetAsync(int id, CancellationToken ct)
    {
        var p = await _db.Permissions.AsNoTracking()
            .Include(x => x.Tools)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        return p is null ? null : ToDto(p);
    }

    public async Task<List<ToolDto>> ListToolsAsync(
        int permissionId, CancellationToken ct)
    {
        return await _db.Tools.AsNoTracking()
            .Where(t => t.PermissionId == permissionId)
            .OrderBy(t => t.Code)
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
            .ToListAsync(ct);
    }

    private static PermissionDto ToDto(Domain.Entities.Permission p) => new()
    {
        Id = p.Id,
        Code = p.Code,
        DisplayName = p.DisplayName,
        Description = p.Description,
        Category = p.Category,
        SortOrder = p.SortOrder,
        IsSystem = p.IsSystem,
        ControllerName = p.ControllerName,
        Tools = p.Tools.Select(t => new ToolDto
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
        }).OrderBy(t => t.Code).ToList(),
    };
}