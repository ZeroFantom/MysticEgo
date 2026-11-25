using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/managed/infrastructure-setup")]
public sealed class InfrastructureSetupController(IConfiguration config, DevelopmentTeam team, NodeState state) : ControllerBase
{
    private const string Install = "/opt/mysticego-infrastructure/runner/run-web.sh";
    private static readonly Regex SafeHost = new(@"\A[A-Za-z0-9][A-Za-z0-9.-]{0,252}\z");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static SetupRun? current;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private IConfigurationSection Targets => config.GetSection("InfrastructureSetup:Targets");

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status()
    {
        if (!team.IsOwner(User)) return Forbid();
        return Ok(new { targets = Targets.GetChildren().Where(t => SafeHost.IsMatch(t["Host"] ?? "")).Select(t => t.Key),
            run = Volatile.Read(ref current) });
    }

    [HttpPost, RequestSizeLimit(4096)]
    public IActionResult Start(SetupInput input)
    {
        if (!team.IsOwner(User)) return Forbid();
        if (!input.Confirmed || input.Component is not ("monitoring" or "metrics-source" or "docker" or "postgres" or "auth-postgres" or "lmstudio" or "model-connect"))
            return BadRequest(new { error = "Выберите компонент и подтвердите установку." });
        var target = Targets.GetChildren().FirstOrDefault(t => t.Key == input.Target);
        var host = target?["Host"] ?? "";
        var key = config["InfrastructureSetup:IdentityFile"];
        var known = config["InfrastructureSetup:KnownHostsFile"];
        if (!SafeHost.IsMatch(host) || key is null || known is null ||
            !Path.IsPathFullyQualified(key) || !Path.IsPathFullyQualified(known) ||
            !System.IO.File.Exists(key) || !System.IO.File.Exists(known))
            return Conflict(new { error = "Сначала настройте доверенный SSH-адрес для панели Orchestra." });
        var port = int.TryParse(target?["Port"], out var configured) ? configured : 22;
        if (port is < 1 or > 65535) return Conflict(new { error = "Неверный порт SSH в конфигурации." });
        if (!Gate.Wait(0)) return Conflict(new { error = "Установка уже выполняется." });
        var actor = User.Identity?.Name ?? "developer";
        Volatile.Write(ref current, new SetupRun(input.Target, input.Component, "running", "Подключаемся к серверу."));
        _ = Task.Run(() => Execute(input, host, port, key, known, actor));
        return Accepted();
    }

    private async Task Execute(SetupInput input, string host, int port, string key, string known, string actor)
    {
        var ok = false;
        var message = "Установка завершилась ошибкой. Проверьте SSH и службы на целевом сервере.";
        try
        {
            var start = new ProcessStartInfo("ssh") { UseShellExecute = false, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-F", "/dev/null", "-i", key, "-o", "IdentitiesOnly=yes", "-o", "BatchMode=yes",
                "-o", "StrictHostKeyChecking=yes", "-o", "UserKnownHostsFile=" + known, "-o", "GlobalKnownHostsFile=/dev/null",
                "-o", "ClearAllForwardings=yes", "-o", "ConnectTimeout=10", "-o", "ServerAliveInterval=20",
                "-o", "ServerAliveCountMax=3", "-p", port.ToString(), "root@" + host, Install })
                start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(45));
            var stdout = LastLines(process.StandardOutput, deadline.Token);
            var stderr = LastLines(process.StandardError, deadline.Token);
            try
            {
                await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input, Json).AsMemory(), deadline.Token);
                process.StandardInput.Close();
                await process.WaitForExitAsync(deadline.Token);
                ok = process.ExitCode == 0;
                var output = await stdout;
                await stderr;
                if (!string.IsNullOrWhiteSpace(output)) message = output;
            }
            finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or UnauthorizedAccessException or
            OperationCanceledException or System.ComponentModel.Win32Exception)
        {
            message = error is OperationCanceledException ? "Истёк тайм-аут установки. Проверьте состояние службы на VPS." : message;
        }
        finally
        {
            if (!string.IsNullOrEmpty(input.Secret))
                message = message.Replace(input.Secret, "[REDACTED]", StringComparison.Ordinal);
            Volatile.Write(ref current, new SetupRun(input.Target, input.Component, ok ? "done" : "failed", message));
            try { state.Log(actor, "infrastructure.setup", input.Component + " " + input.Target, ok); }
            finally { Gate.Release(); }
        }
    }

    private static async Task<string> LastLines(StreamReader input, CancellationToken ct)
    {
        var lines = new Queue<string>();
        while (await input.ReadLineAsync(ct) is { } line)
        {
            if (lines.Count == 12) lines.Dequeue();
            lines.Enqueue(line[..Math.Min(line.Length, 280)]);
        }
        return string.Join('\n', lines);
    }

    public sealed record SetupInput(string Target, string Component, bool Confirmed,
        string? Url, string? Bind, string? Cidr, string? Role, string? Secret);
    private sealed record SetupRun(string Target, string Component, string Status, string Message);
}
