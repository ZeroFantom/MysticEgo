using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/managed/infrastructure-maintenance")]
public sealed class InfrastructureMaintenanceController(DevelopmentTeam team, DatabaseMaintenance database) : ControllerBase
{
    private const string Report = "/var/lib/mysticego-infrastructure-status/status.json";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (!team.IsOwner(User)) return Forbid();
        var components = new List<Component>();
        var installed = false;
        try
        {
            var info = new FileInfo(Report);
            if (info.Exists && info.Length <= 32768)
            {
                var report = JsonSerializer.Deserialize<Snapshot>(await System.IO.File.ReadAllTextAsync(Report, ct), Json);
                if (report?.Components is { } entries)
                {
                    installed = true;
                    components.AddRange(entries.Where(x => x.Name is "orchestra" or "docker" or "lmstudio" or "voice" or "postgres-service").Take(5));
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { }
        var db = database.Snapshot;
        components.Add(new(db.Name, db.State, db.Message, db.CheckedAt));
        if (installed && components.Where(c => c.Name != "database").All(c => DateTimeOffset.UtcNow - c.CheckedAt > TimeSpan.FromMinutes(55)))
            components.Add(new("worker", "degraded", "Worker не публикует свежие проверки. Проверьте systemd-таймер.", DateTimeOffset.UtcNow));
        return Ok(new { installed, components });
    }

    public sealed record Component(string Name, string State, string Message, DateTimeOffset CheckedAt);
    public sealed record Snapshot(Component[] Components);
}
