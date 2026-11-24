using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;

namespace Models.Autorization.Services;

public sealed class ToolService : IToolService
{
    private readonly AuthDbContext _db;
    private readonly IAccessResolver _access;
    private readonly ILogger<ToolService> _log;

    public ToolService(AuthDbContext db, IAccessResolver access, ILogger<ToolService> log)
    {
        _db = db;
        _access = access;
        _log = log;
    }

    public async Task<List<ToolDto>> ListAsync(CancellationToken ct)
    {
        return await _db.Tools.AsNoTracking()
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

    public async Task<bool> SetEnabledAsync(
        int id, bool enabled, CancellationToken ct)
    {
        var tool = await _db.Tools.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (tool is null) return false;

        tool.IsEnabled = enabled;
        tool.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        _access.Invalidate();

        _log.LogInformation("Tool {Code} enabled={Enabled}", tool.Code, enabled);
        return true;
    }
}