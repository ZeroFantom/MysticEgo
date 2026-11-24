using System.Collections.Concurrent;
using System.Diagnostics;
using System.Formats.Tar;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

public sealed record SshTarget(string Host, string User, int Port = 22, string Os = "linux");
public sealed record SshPreflight(bool Ssh, bool Docker, string Detail, DateTimeOffset CheckedAt);
public sealed record DeploymentJob(Guid Id, Guid NodeId, string Name, string Status, string Stage, DateTimeOffset At);

public sealed class ManagedDeployment(ManagedNetwork network, NodeState state, IConfiguration config, ILogger<ManagedDeployment> logger) : BackgroundService
{
    private readonly Channel<(ManagedNode Node, SshTarget Target, Guid Id, string Actor)> queue =
        Channel.CreateBounded<(ManagedNode, SshTarget, Guid, string)>(8);
    private readonly ConcurrentDictionary<Guid, DeploymentJob> jobs = new();
    private readonly object gate = new();
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Folder => Path.Combine(Path.GetDirectoryName(network.ProfilePath(Guid.Empty))!, "jobs");

    public IEnumerable<DeploymentJob> Jobs() => jobs.Values.OrderByDescending(x => x.At).Take(100);

    public async Task<SshPreflight> Preflight(ManagedNode node, SshTarget target, bool developer, CancellationToken ct)
    {
        Validate(node, target, developer);
        var script = target.Os == "windows"
            ? "if (Get-Command docker -ErrorAction SilentlyContinue) { docker info --format '{{.ServerVersion}}' 2>$null; if ($LASTEXITCODE -eq 0) { Write-Output 'MYSTIC_DOCKER_READY' } }; Write-Output 'MYSTIC_SSH_READY'"
            : "if command -v docker >/dev/null 2>&1 && docker info >/dev/null 2>&1; then echo MYSTIC_DOCKER_READY; fi; echo MYSTIC_SSH_READY";
        try
        {
            var result = await Run("ssh", [.. SshArguments(target), Endpoint(target), RemoteScript(target, script)],
                TimeSpan.FromSeconds(12), ct);
            var ssh = result.Code == 0 && result.Text.Contains("MYSTIC_SSH_READY", StringComparison.Ordinal);
            var docker = ssh && result.Text.Contains("MYSTIC_DOCKER_READY", StringComparison.Ordinal);
            return new(ssh, docker, ssh ? "SSH отвечает. Ключ сервера подтверждён." :
                "SSH недоступен. Проверьте адрес, ключ сервера и ключ входа от имени Оркестра.", DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return new(false, false, "SSH-клиент Оркестра отсутствует или время соединения истекло.", DateTimeOffset.UtcNow);
        }
    }

    public DeploymentJob Enqueue(ManagedNode node, SshTarget target, bool developer, string actor)
    {
        Validate(node, target, developer);
        if (node.Kind != "global" || !developer) throw new NodeApiException(403, "Оркестр развёртывает только Global.");
        if (!node.Enabled) throw new NodeApiException(409, "Узел отключён.");
        if (node.Kind is "local" or "agent" && network.Parent(node) is { } parent && network.Report(parent.Id) is null)
            throw new NodeApiException(409, "Дождитесь подключения родительского узла.");
        if (node.Mode == "docker" && target.Os != "linux")
            throw new NodeApiException(400, "Контейнерный запуск этой поставки доступен на Linux x64.");
        lock (gate)
        {
            if (jobs.Values.Any(x => x.NodeId == node.Id && x.Status is "queued" or "running"))
                throw new NodeApiException(409, "Уже выполняется задание для этого узла.");
            var job = new DeploymentJob(Guid.NewGuid(), node.Id, node.Name, "queued", "Ожидание запуска", DateTimeOffset.UtcNow);
            Save(job);
            if (!queue.Writer.TryWrite((node, target, job.Id, actor)))
            {
                Save(job with { Status = "failed", Stage = "Очередь заполнена" });
                throw new NodeApiException(429, "Очередь развёртываний заполнена.");
            }
            return job;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        foreach (var path in Directory.EnumerateFiles(Folder, "*.json"))
        {
            try
            {
                var job = JsonSerializer.Deserialize<DeploymentJob>(await File.ReadAllTextAsync(path, ct), Json);
                if (job is null) continue;
                if (job.Status is "queued" or "running") Save(job with { Status = "interrupted", Stage = "Оркестр перезапущен" });
                else jobs[job.Id] = job;
            }
            catch (JsonException error) { logger.LogWarning(error, "Could not load deployment metadata"); }
        }
        await foreach (var (node, target, id, actor) in queue.Reader.ReadAllAsync(ct))
        {
            try
            {
                Set(id, "running", "Проверка подключения SSH");
                var check = await Preflight(node, target, true, ct);
                if (!check.Ssh || node.Mode == "docker" && !check.Docker)
                    throw new NodeApiException(409, check.Ssh ? "Docker Engine не отвечает на целевом узле." : check.Detail);
                await Push(node, target, id, ct);
                Set(id, "succeeded", "Узел отправил отчёт о готовности");
                state.Log(actor, "deployment.run", node.Id.ToString(), true, $"{node.Kind} · {node.Mode}");
            }
            catch (Exception error)
            {
                var canceled = ct.IsCancellationRequested;
                var message = canceled ? "Оркестр остановлен" : error is NodeApiException ? error.Message :
                    "Развёртывание прервано. Подробности в журнале сервера.";
                if (!canceled) logger.LogWarning(error, "Deployment {JobId} for node {NodeId} failed", id, node.Id);
                Set(id, canceled ? "interrupted" : "failed", message);
                state.Log(actor, "deployment.run", node.Id.ToString(), false, message);
            }
        }
    }

    private async Task Push(ManagedNode node, SshTarget target, Guid job, CancellationToken ct)
    {
        var root = config["Managed:PackageRoot"];
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(Path.Combine(root, "runtime", "app")))
            throw new NodeApiException(503, "Оркестру недоступен пакет приложений. Запустите его из полного архива MysticEgo.");
        var station = node.Id.ToString("N");
        var release = job.ToString("N");
        var remoteDir = $".mysticego/stations/{station}";
        var remoteRelease = $"{remoteDir}/{release}";
        var localArchive = Path.Combine(Folder, release + ".tar");
        try
        {
            Set(job, "running", "Подготовка пакета и профиля");
            await MakeTar(root, target.Os, node.Kind, localArchive, ct);
            var dirScript = target.Os == "windows"
                ? $"$ErrorActionPreference='Stop'; New-Item -ItemType Directory -Force -Path (Join-Path $HOME '{remoteRelease}') | Out-Null"
                : $"umask 077; mkdir -p ~/{remoteRelease}";
            await Checked("ssh", [.. SshArguments(target), Endpoint(target), RemoteScript(target, dirScript)], 20, ct);
            Set(job, "running", "Передача пакета по SSH");
            await Checked("scp", [.. SshArguments(target, true), localArchive, $"{Endpoint(target)}:{remoteRelease}/package.tar"], 420, ct);
            await Checked("scp", [.. SshArguments(target, true), network.ProfilePath(node.Id), $"{Endpoint(target)}:{remoteRelease}/station.json"], 30, ct);
            Set(job, "running", "Запуск и ожидание отчёта");
            var script = target.Os == "windows" ? WindowsScript(station, remoteDir, remoteRelease) :
                LinuxScript(station, remoteDir, remoteRelease, node.Mode == "docker");
            var launchedAt = DateTimeOffset.UtcNow;
            await Checked("ssh", [.. SshArguments(target), Endpoint(target), RemoteScript(target, script)], 240, ct);
            for (var attempt = 0; attempt < 40; attempt++)
            {
                if (network.Report(node.Id) is { } report && report.Seen >= launchedAt && report.Healthy.Contains(node.Kind)) return;
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            throw new NodeApiException(504, "Пакет передан. Узел не подтвердил запуск. Проверьте адреса Auth и Оркестра на целевом ПК.");
        }
        finally { if (File.Exists(localArchive)) File.Delete(localArchive); }
    }

    private static async Task MakeTar(string root, string os, string role, string destination, CancellationToken ct)
    {
        await using var file = File.Create(destination);
        await using var tar = new TarWriter(file, TarEntryFormat.Pax, leaveOpen: true);
        await AddDirectory("runtime/app/bin");
        await AddDirectory("runtime/app/settings");
        await AddDirectory("runtime/app/web/" + role);
        if (role is "agent" or "audit")
        {
            await AddDirectory("runtime/vision");
            await AddDirectory("runtime/avatars");
        }
        if (os == "windows") await AddDirectory("runtime/win-x64");
        else await tar.WriteEntryAsync(Path.Combine(root, "runtime", "linux-x64.tar.gz"), "runtime/linux-x64.tar.gz", ct);
        async Task AddDirectory(string relative)
        {
            foreach (var path in Directory.EnumerateFiles(Path.Combine(root, relative), "*", SearchOption.AllDirectories))
                await tar.WriteEntryAsync(path, Path.GetRelativePath(root, path).Replace('\\', '/'), ct);
        }
    }

    private static string LinuxScript(string id, string dir, string release, bool docker)
    {
        var unpack = $"set -eu; umask 077; cd ~/{release}; tar -xf package.tar; rm package.tar; mkdir -p ~/{dir}/data; ";
        var installer = "runtime/app/bin/MysticEgo.Installer.dll";
        if (docker) return unpack +
            $"docker rm -f mysticego-{id} >/dev/null 2>&1 || true; " +
            $"docker run -d --restart unless-stopped --network host --name mysticego-{id} --mount type=bind,source=/etc/machine-id,target=/run/mysticego-host-machine-id,readonly --mount type=bind,source=\"$PWD\",target=/app,readonly --mount type=bind,source=\"$HOME/{dir}/data\",target=/data -w /app mcr.microsoft.com/dotnet/aspnet:10.0 dotnet {installer} --join /app/station.json --root /app --output /data";

        return unpack +
            $"mkdir -p runtime/linux; tar -xzf runtime/linux-x64.tar.gz -C runtime/linux; " +
            $"old=~/{dir}/runner.pid; if [ -f \"$old\" ]; then cat \"$old\" | xargs -r kill 2>/dev/null || true; sleep 2; fi; " +
            $"nohup \"$PWD/runtime/linux/dotnet\" {installer} --join \"$PWD/station.json\" --root \"$PWD\" --output \"$HOME/{dir}/data\" </dev/null >> \"$HOME/{dir}/launcher.log\" 2>&1 & echo $! > \"$old\"";
    }

    private static string WindowsScript(string id, string dir, string release) =>
        $"$ErrorActionPreference='Stop'; Set-Location (Join-Path $HOME '{release}'); tar.exe -xf package.tar; if ($LASTEXITCODE) {{ throw 'tar' }}; " +
        $"Remove-Item package.tar; $p=(Get-Location).Path; $d=Join-Path $HOME '{dir}/data'; New-Item -ItemType Directory -Force -Path $d | Out-Null; " +
        $"$a=New-ScheduledTaskAction -Execute (Join-Path $p 'runtime/win-x64/dotnet.exe') -WorkingDirectory $p -Argument ('runtime/app/bin/MysticEgo.Installer.dll --join station.json --root . --output \"'+$d+'\"'); " +
        $"$name='MysticEgo-{id}'; if(Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue) {{ Stop-ScheduledTask -TaskName $name; Start-Sleep -Seconds 2 }}; " +
        "$u=[Security.Principal.WindowsIdentity]::GetCurrent().Name; $t=New-ScheduledTaskTrigger -AtLogOn -User $u; " +
        "Register-ScheduledTask -TaskName $name -User $u -Action $a -Trigger $t -Force | Out-Null; Start-ScheduledTask -TaskName $name";

    private void Validate(ManagedNode node, SshTarget target, bool developer)
    {
        if (target.Host is null || target.User is null || target.Os is not ("windows" or "linux") || target.Port is < 1 or > 65535 ||
            !Regex.IsMatch(target.User, "^[a-zA-Z0-9_][a-zA-Z0-9_.-]{0,63}$") ||
            !Regex.IsMatch(target.Host, "^[a-zA-Z0-9][a-zA-Z0-9.-]{0,252}$"))
            throw new NodeApiException(400, "Введите имя или IPv4-адрес сервера, имя SSH-пользователя и порт.");
        var allowed = config.GetSection("Managed:SshTargets").Get<string[]>() ?? [];
        if (!developer && !allowed.Contains(Endpoint(target), StringComparer.OrdinalIgnoreCase) ||
            !new Uri(node.PublicUrl).Host.Equals(target.Host, StringComparison.OrdinalIgnoreCase) && !allowed.Contains(Endpoint(target), StringComparer.OrdinalIgnoreCase))
            throw new NodeApiException(403, "Укажите SSH-адрес узла из профиля или разрешённый адрес из настроек Оркестра.");
    }

    private static string Endpoint(SshTarget target) => $"{target.User}@{target.Host}";
    private static string RemoteScript(SshTarget target, string script) => target.Os == "windows"
        ? "powershell -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script)) : script;

    private string[] SshArguments(SshTarget target, bool scp = false)
    {
        var args = new List<string> { "-o", "BatchMode=yes", "-o", "StrictHostKeyChecking=yes", "-o", "ConnectTimeout=6",
            "-o", "ClearAllForwardings=yes", scp ? "-P" : "-p", target.Port.ToString() };
        if (config["Managed:SshIdentity"] is { Length: > 0 } identity) args.AddRange(["-i", identity, "-o", "IdentitiesOnly=yes"]);
        if (config["Managed:SshKnownHosts"] is { Length: > 0 } trusted) args.AddRange(["-o", "UserKnownHostsFile=" + trusted]);
        return args.ToArray();
    }

    private async Task Checked(string command, string[] args, int seconds, CancellationToken ct)
    {
        var result = await Run(command, args, TimeSpan.FromSeconds(seconds), ct);
        if (result.Code == 0) return;
        logger.LogWarning("Deployment step {Command} returned {ExitCode}: {Output}", command, result.Code, result.Text[..Math.Min(2000, result.Text.Length)]);
        throw new NodeApiException(502, $"Этап {command} завершился с кодом {result.Code}. Проверьте журнал сервера.");
    }

    private static async Task<(int Code, string Text)> Run(string exe, string[] args, TimeSpan duration, CancellationToken ct)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limit.CancelAfter(duration);
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Could not start OpenSSH");
        var stdout = process.StandardOutput.ReadToEndAsync(limit.Token);
        var stderr = process.StandardError.ReadToEndAsync(limit.Token);
        try
        {
            await process.WaitForExitAsync(limit.Token);
            return (process.ExitCode, (await stdout) + (await stderr));
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }

    private void Set(Guid id, string status, string stage) => Save(jobs[id] with { Status = status, Stage = stage });
    private void Save(DeploymentJob job)
    {
        Directory.CreateDirectory(Folder);
        var path = Path.Combine(Folder, job.Id.ToString("N") + ".json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(job, Json));
        File.Move(temp, path, true);
        jobs[job.Id] = job;
    }
}
