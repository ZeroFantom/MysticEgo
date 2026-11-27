using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

/// <summary>
/// Displays the update worker's state and queues the exact release confirmed by the managing developer.
/// </summary>
[ApiController, Authorize(Policy = "operator"), Route("api/v1/orchestra/update")]
public sealed class OrchestraUpdateController(NodeState state, DevelopmentTeam team, DatabaseMaintenance database, ServerTelemetry telemetry, ServiceLoadMonitor serviceLoads, OptionalDependencyState peers) : ControllerBase
{
    private const string StateDirectory = "/var/lib/mysticego-orchestra-updater";
    private const string Inbox = "/var/lib/mysticego/update-inbox";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static UpdateStatus? ReadStatus()
    {
        try
        {
            var file = new FileInfo(StateDirectory + "/status.json");
            if (!file.Exists || file.Length > 32768) return null;
            var status = JsonSerializer.Deserialize<UpdateStatus>(System.IO.File.ReadAllText(file.FullName), Json);
            return status is null ? null : status with { Maintenance = ReadMaintenance() };
        }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException or JsonException) { return null; }
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

    public sealed record MaintenancePlan(string Action,string Reason,UpdateStatus? Update,
        InfrastructureMaintenanceController.Component[] Components,ServerSample? Host,
        ServiceLoadMonitor.Sample[] Services,DateTimeOffset CheckedAt,bool AutomaticEnabled,bool AutomaticAvailable);
    public sealed record PlanApproval(string Action,bool Confirmed,string? Tag=null,string? Digest=null);

    /// <summary>Only authoritative fresh component failure or a newer published release permits a mutation.</summary>
    public static string Decide(bool busy,bool updateAvailable,bool repairNeeded,bool repairAvailable,bool knownHealthy)
        =>busy?"busy":updateAvailable?"update":repairNeeded?(repairAvailable?"repair":"unavailable"):knownHealthy?"healthy":"unknown";

    private async Task<MaintenancePlan> BuildPlan(CancellationToken ct)
    {
        var now=DateTimeOffset.UtcNow;
        var update=ReadStatus();
        var report=await InfrastructureMaintenanceController.ReadAsync(database,ct);
        var recovery=ReadRecovery();
        var maintenance=update?.Maintenance??ReadMaintenance();
        var updateFresh=now-System.IO.File.GetLastWriteTimeUtc(StateDirectory+"/status.json")<TimeSpan.FromMinutes(3);
        var pending=new[]{"pending.json","maintenance-rescue.json","maintenance-repair.json"}.Any(n=>System.IO.File.Exists(Path.Combine(Inbox,n)));
        var busy=pending||serviceLoads.Read().Any(s=>s.Unit=="mysticego-infrastructure-reconcile.service"&&s.ActiveState=="activating")||update?.Phase is "switching" or "downloading"||
            recovery?.State=="repairing"&&now-recovery.CheckedAt<TimeSpan.FromMinutes(10)||
            report.Components.Any(c=>c.State=="repairing"&&now-c.CheckedAt<TimeSpan.FromMinutes(55));
        var failed=report.Components.Any(c=>c.Name is ("docker" or "lmstudio" or "voice" or "postgres-service" or "worker")&&c.State=="degraded"&&now-c.CheckedAt<TimeSpan.FromMinutes(55));
        var repairNeeded=OperatingSystem.IsLinux()&&(!report.SocketAvailable||failed||maintenance?.State is "degraded" or "unavailable");
        var repairAvailable=Directory.Exists(Inbox)&&(recovery is not null||maintenance is not null&&now-maintenance.CheckedAt<TimeSpan.FromMinutes(5));
        var available=updateFresh&&Directory.Exists(Inbox)&&update is {Phase:"ready" or "failed",Protocol:2,Candidate:{ } candidate}&&
            update.Targets?.Contains("orchestra")==true&&candidate.Tag!=update.Current;
        var healthy=updateFresh&&update?.Phase=="current"&&report.Installed&&report.SocketAvailable&&
            new[]{"orchestra","docker","lmstudio","voice","postgres-service","database"}.All(name=>report.Components.Any(c=>c.Name==name&&(c.State is "available" or "external" or "disabled")&&now-c.CheckedAt<TimeSpan.FromMinutes(3)));
        var action=Decide(busy,available,repairNeeded,repairAvailable,healthy);
        var reason=action switch
        {
            "busy"=>"Обслуживание или обновление уже выполняется.",
            "update"=>"Доступен подтверждённый новый релиз Orchestra.",
            "repair"=>"Обнаружена неисправность службы обслуживания или её компонентов.",
            "healthy"=>"Все проверяемые службы готовы, обновление не требуется.",
            "unavailable"=>"Канал восстановления недоступен. Используйте доверенный CloudDesk.",
            _=>"Недостаточно актуальных данных. Ожидается проверка."
        };
        var components=report.Components.Concat(peers.Snapshot().Where(p=>p.Name!="orchestra").Select(p=>new InfrastructureMaintenanceController.Component(p.Name,p.State,"HTTP health observation",p.CheckedAt,p.Reason))).ToArray();
        return new(action,reason,update,components,telemetry.History().LastOrDefault(),serviceLoads.Read(),now,AutoEnabled(),Directory.Exists(Inbox));
    }

    private static bool AutoEnabled()=>true;

    [HttpGet("automatic"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public IActionResult Automatic()=>!team.IsOwner(User)?Forbid():Ok(new{enabled=true,available=Directory.Exists(Inbox)});
    public sealed record AutoPolicy(bool Enabled,bool Confirmed);
    [HttpPost("automatic"),RequestSizeLimit(1024)]
    public IActionResult ConfigureAutomatic(AutoPolicy policy)
    {
        if(!team.IsOwner(User))return Forbid();
        return Conflict(new{error="Автоматическое обновление является обязательной политикой Orchestra.",enabled=true});
    }

    [HttpGet("plan"), ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
    public async Task<IActionResult> Plan(CancellationToken ct)
        =>!team.IsOwner(User)?Forbid():Ok(await BuildPlan(ct));

    [HttpPost("plan"),RequestSizeLimit(4096)]
    public async Task<IActionResult> ExecutePlan(PlanApproval request,CancellationToken ct)
    {
        if(!team.IsOwner(User))return Forbid();
        var plan=await BuildPlan(ct);
        if(!request.Confirmed||request.Action!=plan.Action)
            return Conflict(new{error="Состояние изменилось. Повторите проверку."});
        return plan.Action switch
        {
            "update" when request.Tag is not null&&request.Digest is not null=>Approve(new(request.Tag,request.Digest,true,"orchestra")),
            "repair"=>QueueRepair(),
            _=>Conflict(new{error="Обновление или восстановление не требуется либо уже выполняется."})
        };
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
    public async Task<IActionResult> RepairMaintenance(CancellationToken ct)
    {
        if (!team.IsOwner(User)) return Forbid();
        if((await BuildPlan(ct)).Action!="repair")return Conflict(new{error="Восстановление сейчас не требуется или недоступно."});
        return QueueRepair();
    }

    private IActionResult QueueRepair()
    {
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
            (target=="orchestra"&&candidate.Tag==status.Current)||(target=="audit"&&candidate.Tag==status.CurrentAudit)||
            (target=="both"&&candidate.Tag==status.Current&&candidate.Tag==status.CurrentAudit)||
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

