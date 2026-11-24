using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Models.Autorization.Configuration;
using Models.Autorization.Data;

namespace Models.Autorization.Services;

public sealed class AccessResolver : IAccessResolver
{
    private static int _version;

    private readonly AuthDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly AuthorizationOptions _opt;

    public AccessResolver(AuthDbContext db, IMemoryCache cache, AuthorizationOptions opt)
    {
        _db = db;
        _cache = cache;
        _opt = opt;
    }

    public static void BumpVersion() => Interlocked.Increment(ref _version);

    public void Invalidate() => BumpVersion();

    public void InvalidateUser(Guid userId)
    {
        _cache.Remove($"user-access:{userId}:v{Volatile.Read(ref _version)}");
    }

    public async Task<ToolAccessInfo?> GetToolByKeyAsync(string key, CancellationToken ct = default)
    {
        var v = Volatile.Read(ref _version);
        var cacheKey = $"tool-key:{key}:v{v}";

        if (_cache.TryGetValue(cacheKey, out ToolAccessInfo? cached))
            return cached;

        var tool = await _db.Tools.AsNoTracking()
            .Where(t => t.Code == key)
            .Select(t => new ToolAccessInfo(t.Id, t.Code, t.IsEnabled, t.IsPublic, t.PermissionId))
            .FirstOrDefaultAsync(ct);

        if (tool is not null)
            _cache.Set(cacheKey, tool, TimeSpan.FromMinutes(_opt.PermissionCacheMinutes));

        return tool;
    }

    public async Task<ToolAccessInfo?> GetToolByIdAsync(int id, CancellationToken ct = default)
    {
        var v = Volatile.Read(ref _version);
        var cacheKey = $"tool-id:{id}:v{v}";

        if (_cache.TryGetValue(cacheKey, out ToolAccessInfo? cached))
            return cached;

        var tool = await _db.Tools.AsNoTracking()
            .Where(t => t.Id == id)
            .Select(t => new ToolAccessInfo(t.Id, t.Code, t.IsEnabled, t.IsPublic, t.PermissionId))
            .FirstOrDefaultAsync(ct);

        if (tool is not null)
            _cache.Set(cacheKey, tool, TimeSpan.FromMinutes(_opt.PermissionCacheMinutes));

        return tool;
    }

    public async Task<UserAccessSnapshot> GetUserAccessAsync(
        Guid userId, CancellationToken ct = default)
    {
        var v = Volatile.Read(ref _version);
        var cacheKey = $"user-access:{userId}:v{v}";

        if (_cache.TryGetValue(cacheKey, out UserAccessSnapshot? cached) && cached is not null)
            return cached;

        var userRoles = await _db.UserRoles.AsNoTracking()
            .Where(ur => ur.UserId == userId)
            .Include(ur => ur.Role)
            .ThenInclude(r => r.RolePermissions)
            .ThenInclude(rp => rp.Permission)
            .ThenInclude(p => p.Tools)
            .ToListAsync(ct);

        var roles = userRoles.Select(ur => ur.Role.Code).Distinct().ToList();

        var permissionIds = new HashSet<int>();
        var toolIds = new HashSet<int>();
        var hasBypass = false;
        var maxPriority = int.MinValue;

        foreach (var ur in userRoles)
        {
            if (ur.Role.Priority > maxPriority)
                maxPriority = ur.Role.Priority;

            foreach (var rp in ur.Role.RolePermissions)
            {
                permissionIds.Add(rp.PermissionId);

                if (string.Equals(rp.Permission.Code,
                        "system.bypass", StringComparison.OrdinalIgnoreCase))
                    hasBypass = true;

                foreach (var t in rp.Permission.Tools)
                    toolIds.Add(t.Id);
            }
        }

        if (maxPriority == int.MinValue) maxPriority = 0;

        var snapshot = new UserAccessSnapshot(
            userId, hasBypass, maxPriority,
            roles, permissionIds, toolIds);

        _cache.Set(cacheKey, snapshot,
            TimeSpan.FromMinutes(_opt.UserPermissionCacheMinutes));

        return snapshot;
    }
}