using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/orchestra/diagnostics/report")]
public sealed class DiagnosticReportController(DevelopmentTeam team, DatabaseMaintenance database,
    ServerTelemetry telemetry, OptionalDependencyState dependencies, ManagedNetwork network) : ControllerBase
{
    private static string SafeState(string? value) => value is "available" or "degraded" or "unavailable" or "repairing"
        or "checking" or "disabled" or "external" or "missing" or "waiting" or "unreachable" or "unreadable"
        or "reporting" or "current" or "ready" or "downloading" or "switching" or "failed" or "running" or "done"
        ? value : "unknown";
    private static string SafeReason(string? value) => value is "invalid-upstream-url" or "upstream-http-error" or "timeout" or "connection-failed" ? value : "";
    private static string SafeTag(string? value) => value is not null && value.Length <= 90 &&
        System.Text.RegularExpressions.Regex.IsMatch(value, @"\Av[0-9][a-zA-Z0-9._-]*\z") ? value : "unknown";
    private static string SafeName(string value) => value is "auth" or "orchestra" or "global" or "local" or "audit" or "agent"
        or "database" or "docker" or "lmstudio" or "voice" or "postgres-service" or "worker" or "metrics-source" or "monitoring"
        or "postgres" or "postgres-native" or "prepare-postgres" or "auth-postgres" or "model-connect" ? value : "other";
    private static object?[] Component(string origin, InfrastructureMaintenanceController.Component c) =>
        [origin, SafeName(c.Name), SafeState(c.State), c.CheckedAt, null, null, ""];
    private static object?[] Database(DatabaseMaintenance.Result c) =>
        ["database-history", "database", SafeState(c.State), c.CheckedAt, null, null,
            c.Message.StartsWith("PostgreSQL:", StringComparison.Ordinal) ? "PostgreSQL" :
            c.Message.StartsWith("SQLite:", StringComparison.Ordinal) ? "SQLite" : "unknown"];
    private static object?[] Peer(string origin, CapabilityHealth c) =>
        [origin, SafeName(c.Name), SafeState(c.State), c.CheckedAt, c.HttpStatus, c.LatencyMs, SafeReason(c.Reason)];

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Download([FromQuery] string format = "json", CancellationToken ct = default)
    {
        if (!team.IsOwner(User)) return Forbid();
        if (format is not ("json" or "txt" or "xlsx")) return BadRequest(new { error = "Выберите JSON, TXT или XLSX." });
        var at = DateTimeOffset.UtcNow;
        var maintenance = await InfrastructureMaintenanceController.ReadAsync(database, ct);
        var checks = new List<object?[]>();
        checks.AddRange(maintenance.Components.Select(c => Component("latest", c)));
        checks.AddRange(maintenance.History.Select(c => Component("host-history", c)));
        checks.AddRange(database.History().Select(Database));
        checks.AddRange(dependencies.Snapshot().Select(c => Peer("peer-latest", c)));
        checks.AddRange(dependencies.History().Select(c => Peer("peer-history", c)));
        checks.Add(["report", "worker", SafeState(maintenance.WorkerState), at, null, null,
            maintenance.Source is "host-file" or "host-socket" ? maintenance.Source : "no-report"]);
        var metadata = new List<object?[]>
        {
            new object?[] {"schemaVersion", 1}, new object?[] {"generatedUtc", at},
            new object?[] {"os", RuntimeInformation.OSDescription},
            new object?[] {"processors", Environment.ProcessorCount},
            new object?[] {"workerReportReceived", maintenance.Installed},
            new object?[] {"localSocketPresent", maintenance.SocketAvailable},
            new object?[] {"databaseProvider", Database(database.Snapshot)[6]},
            new object?[] {"scope", "Runtime diagnostics; bounded retained samples, not CI or raw system logs"},
            new object?[] {"privacy", "No credentials, URLs, node IDs, user identities, messages, raw installer output or raw journal"}
        };
        try
        {
            var update = OrchestraUpdateController.ReadStatus();
            metadata.Add(["installedRelease", SafeTag(update?.Current)]);
            metadata.Add(["candidateRelease", SafeTag(update?.Candidate?.Tag)]);
            metadata.Add(["updateState", SafeState(update?.Phase)]);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { metadata.Add(["updateState", "unreadable"]); }
        if (InfrastructureSetupController.DiagnosticRun() is { } setup)
            metadata.Add(["lastSetup", SafeName(setup.Component) + ": " + SafeState(setup.Status)]);
        else metadata.Add(["lastSetup", "no-retained-run"]);
        var samples = telemetry.History();
        var links = new List<object?[]>();
        // Sequential aliases intentionally avoid exporting device names, identifiers and addresses.
        try
        {
            var i = 0;
            foreach (var node in network.Nodes().Take(2000))
            {
                var report = network.Report(node.Id);
                links.Add([++i, SafeName(node.Kind), node.Enabled, report?.Healthy.Contains(node.Kind) == true,
                    report?.Link?.LatencyMs, report?.Link?.Mbps, report?.Link?.MeasuredAt]);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        { metadata.Add(["linksState", "unreadable"]); }
        var tables = new DiagnosticReport.Table[]
        {
            new("Summary", ["field","value"], metadata.ToArray()),
            new("Checks", ["source","component","state","checkedUtc","httpStatus","latencyMs","reasonOrProvider"], checks.ToArray()),
            new("Telemetry", ["atUtc","cpuPercent","memoryBytes","managedBytes","memoryLimitBytes","rxBytesPerSecond","txBytesPerSecond","diskFreeBytes","diskTotalBytes","uptimeSeconds","hostCpuPercent","hostMemoryUsedBytes","hostMemoryTotalBytes"],
                samples.Select(s => new object?[] {s.At,s.CpuPercent,s.MemoryBytes,s.ManagedBytes,s.MemoryLimitBytes,s.RxBytesPerSecond,s.TxBytesPerSecond,s.DiskFreeBytes,s.DiskTotalBytes,s.UptimeSeconds,s.HostCpuPercent,s.HostMemoryUsedBytes,s.HostMemoryTotalBytes}).ToArray()),
            new("Links", ["alias","kind","enabled","online","latencyMs","mbps","measuredUtc"], links.ToArray())
        };
        var content = DiagnosticReport.Encode(tables, format);
        var mime = format == "xlsx" ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" : format == "json" ? "application/json; charset=utf-8" : "text/plain; charset=utf-8";
        return File(content, mime, "MysticEgo-diagnostics-" + at.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "Z." + format);
    }
}
