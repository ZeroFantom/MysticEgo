using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

/// <summary>
/// Displays the update worker's state and queues the exact release confirmed by the managing developer.
/// </summary>
[ApiController, Authorize(Policy = "operator"), Route("api/v1/orchestra/update")]
public sealed class OrchestraUpdateController(NodeState state, DevelopmentTeam team) : ControllerBase
{
    private const string StateDirectory = "/var/lib/mysticego-orchestra-updater";
    private const string Inbox = "/var/lib/mysticego/update-inbox";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static UpdateStatus? ReadStatus()
    {
        if (!System.IO.File.Exists(StateDirectory + "/status.json")) return null;
        var status = JsonSerializer.Deserialize<UpdateStatus>(System.IO.File.ReadAllText(StateDirectory + "/status.json"), Json);
        if (status is null) return null;
        return status with { Maintenance = ReadMaintenance() };
    }

    internal static MaintenanceStatus? ReadMaintenance()
    {
        try
        {
            var path = new FileInfo(StateDirectory + "/maintenance.json");
            if (!path.Exists || path.Length > 8192) return null;
            return JsonSerializer.Deserialize<MaintenanceStatus>(System.IO.File.ReadAllText(path.FullName), Json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    internal static RecoveryStatus? ReadRecovery()
    {
        try
        {
            var path = new FileInfo(StateDirectory + "/rescue.json");
            if (!path.Exists || path.Length > 8192) return null;
            return JsonSerializer.Deserialize<RecoveryStatus>(System.IO.File.ReadAllText(path.FullName), Json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    [HttpGet("maintenance"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Recovery()
    {
        if (!team.IsOwner(User)) return Forbid();
        var independent = ReadRecovery();
        var maintenance = ReadMaintenance();
        var automatic = maintenance is not null && DateTimeOffset.UtcNow - maintenance.CheckedAt < TimeSpan.FromMinutes(5);
        return Ok(new { independent, automatic, queued = System.IO.File.Exists(Path.Combine(Inbox, "maintenance-rescue.json")) || System.IO.File.Exists(Path.Combine(Inbox, "maintenance-repair.json")), available = Directory.Exists(Inbox) && (independent is not null || automatic) });
    }

    [HttpPost("maintenance"), RequestSizeLimit(1024)]
    public IActionResult RepairMaintenance()
    {
        if (!team.IsOwner(User)) return Forbid();
        var independent = ReadRecovery();
        var maintenance = ReadMaintenance();
        var automatic = maintenance is not null && DateTimeOffset.UtcNow - maintenance.CheckedAt < TimeSpan.FromMinutes(5);
        if (!Directory.Exists(Inbox) || (independent is null && !automatic))
            return Conflict(new { error = "Привилегированный канал восстановления не подтверждён. Откройте CloudDesk → Обновление узлов → Восстановить службу обслуживания (без переустановки)." });
        var path = Path.Combine(Inbox, independent is not null ? "maintenance-rescue.json" : "maintenance-repair.json");
        var temporary = Path.Combine(Inbox, Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            System.IO.File.WriteAllText(temporary, "{\"confirmed\":true}");
            try { System.IO.File.Move(temporary, path); }
            catch (IOException) when (System.IO.File.Exists(path)) { return Accepted(new { accepted = true }); }
            return Accepted(new { accepted = true });
        }
        finally { System.IO.File.Delete(temporary); }
    }

    /// <summary>Reads the latest status published by the update worker.</summary>
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status()
    {
        if (!team.IsOwner(User)) return Forbid();
        var status = ReadStatus();
        return status is null ? StatusCode(503, new { error = "Служба обновления Orchestra ещё не настроена." }) : Ok(status);
    }

    /// <summary>Validates approval and submits a single pending request to the worker.</summary>
    [HttpPost, RequestSizeLimit(4096)]
    public IActionResult Approve(Approval request)
    {
        if (!team.IsOwner(User)) return Forbid();
        var status = ReadStatus();
        var target = request.Target ?? "orchestra";
        if (target is not ("orchestra" or "audit" or "both"))
            return BadRequest(new { error = "Выберите Orchestra, Audit или оба компонента." });
        var allowed = status?.Protocol == 2
            ? status.Targets?.Contains(target, StringComparer.Ordinal) == true
            : target == "orchestra";
        if (!request.Confirmed || !allowed || status?.Candidate is not { } candidate ||
            status.Phase is not ("ready" or "failed") || candidate.Tag != request.Tag || candidate.Digest != request.Digest)
            return Conflict(new { error = "Заново проверьте релиз и подтвердите его версию." });

        var path = Path.Combine(Inbox, "pending.json");
        var temporary = Path.Combine(Inbox, Guid.NewGuid().ToString("N") + ".tmp");
        if (!Directory.Exists(Inbox)) return StatusCode(503, new { error = "Служба обновления не настроена." });
        try
        {
            System.IO.File.WriteAllText(temporary, status.Protocol == 2
                ? JsonSerializer.Serialize(new UpdateRequest(target, candidate), Json)
                : JsonSerializer.Serialize(candidate, Json));
            // Publish only a complete request and preserve any request already queued.
            try
            {
                System.IO.File.Move(temporary, path);
            }
            catch (IOException) when (System.IO.File.Exists(path))
            {
                return Conflict(new { error = "Обновление уже ожидает запуска." });
            }
            state.Log(User.Identity?.Name ?? "developer", "orchestra.update.approved", candidate.Tag + " " + target, true);
            return Accepted(new { accepted = true, target, tag = candidate.Tag });
        }
        finally
        {
            System.IO.File.Delete(temporary);
        }
    }

    public sealed record Approval(string Tag, string Digest, bool Confirmed, string? Target = null);
    private sealed record UpdateRequest(string Target, Candidate Candidate);
    public sealed record Candidate(string Tag, string Digest, string Asset, long Id, long Size);
    public sealed record UpdateStatus(string Phase, string? Current, Candidate? Candidate, string? Message,
        int Protocol = 0, string? CurrentAudit = null, string[]? Targets = null, string? LastTarget = null, MaintenanceStatus? Maintenance = null);
    public sealed record MaintenanceStatus(string State, string? Release, string? Reason, DateTimeOffset CheckedAt, double RetryAfter);
    public sealed record RecoveryStatus(string State, string? Reason, DateTimeOffset CheckedAt);
}

