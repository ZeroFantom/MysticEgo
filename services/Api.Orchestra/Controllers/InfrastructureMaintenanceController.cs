using System.Text.Json;
using System.Net.Sockets;
using System.Text;
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
        var snapshot = await ReadAsync(database, ct);
        return Ok(new { snapshot.Installed, snapshot.Components, snapshot.WorkerState, snapshot.Source, snapshot.SocketAvailable });
    }

    internal static async Task<MaintenanceReport> ReadAsync(DatabaseMaintenance database, CancellationToken ct)
    {
        var components = new List<Component>();
        var installed = false;
        var socketAvailable = OperatingSystem.IsLinux() && System.IO.File.Exists("/var/lib/mysticego/local-setup.sock");
        var source = "none";
        var workerState = "missing";
        Snapshot? snapshot = null;
        try
        {
            var info = new FileInfo(Report);
            if (info.Exists && info.Length <= 131072)
            {
                snapshot = JsonSerializer.Deserialize<Snapshot>(await System.IO.File.ReadAllTextAsync(Report, ct), Json);
                if (snapshot?.Components is { Length: > 0 }) source = "host-file";
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException) { workerState = "unreadable"; }
        if (snapshot?.Components is not { Length: > 0 } && socketAvailable)
        {
            workerState = "waiting";
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(3));
                using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                await socket.ConnectAsync(new UnixDomainSocketEndPoint("/var/lib/mysticego/local-setup.sock"), deadline.Token);
                using var stream = new NetworkStream(socket);
                await stream.WriteAsync(Encoding.UTF8.GetBytes("maintenance-status\n"), deadline.Token);
                using var reader = new StreamReader(stream);
                var line = await reader.ReadLineAsync(deadline.Token);
                if (line is { Length: <= 200000 } &&
                    JsonSerializer.Deserialize<SocketReport>(line, Json) is { Ok: true, Report: { } value })
                { snapshot = value; source = "host-socket"; }
            }
            catch (Exception error) when (error is SocketException or IOException or JsonException or OperationCanceledException)
            { workerState = "unreachable"; }
        }
        if (snapshot?.Components is { Length: > 0 } entries)
        {
            installed = true;
            workerState = "reporting";
            components.AddRange(entries.Where(x => x.Name is "orchestra" or "docker" or "lmstudio" or "voice" or "postgres-service").Take(5));
        }
        var db = database.Snapshot;
        components.Add(new(db.Name, db.State, db.Message, db.CheckedAt));
        if (installed && components.Where(c => c.Name != "database").All(c => DateTimeOffset.UtcNow - c.CheckedAt > TimeSpan.FromMinutes(55)))
            components.Add(new("worker", "degraded", "Служба обслуживания Orchestra не публикует свежие проверки. Будет выполнена повторная проверка запуска.", DateTimeOffset.UtcNow));
        return new MaintenanceReport(installed, components.ToArray(), workerState, source, socketAvailable, snapshot?.History?.Where(c => c.Name is "orchestra" or "docker" or "lmstudio" or "voice" or "postgres-service").TakeLast(120).ToArray() ?? []);
    }

    public sealed record Component(string Name, string State, string Message, DateTimeOffset CheckedAt, string? Reason = null);
    public sealed record Snapshot(Component[] Components, Component[]? History = null);
    internal sealed record MaintenanceReport(bool Installed, Component[] Components, string WorkerState, string Source, bool SocketAvailable, Component[] History);
    private sealed record SocketReport(bool Ok, Snapshot? Report);
}
