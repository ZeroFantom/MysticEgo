using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
namespace MysticEgo.Distributed;

public sealed class AgentUpdateOptions
{
    public bool Enabled {get;set;} = false;
    public bool AutoInstall {get;set;} = false;
    public string CurrentVersion {get;set;} = "0.5.0";
    public string PublicKeyPath {get;set;} = "";
    public string LocalPublicKeyPath {get;set;} = "";
    public string PrivateKeyPath {get;set;} = "";
    public string ReleasesPath {get;set;} = "data/agent-releases";
    public string StagingPath {get;set;} = "data/agent-update-staging";
    public string ServiceName {get;set;} = "MysticEgo.Agent";
    public int PollMinutes {get;set;} = 30;
}
public sealed class AgentUpdateState
{
    public string Status {get;set;}="Ожидание";
    public string? Version {get;set;}
    public string? Error {get;set;}
    public DateTimeOffset? CheckedAt {get;set;}
}
public interface IAgentReleaseService
{
    string? LatestPath();
    string? PackagePath(string? version = null);
    Task<AgentRelease> Publish(IFormFile file,string version,CancellationToken ct);
}
public sealed class AgentReleaseService(AgentUpdateOptions options):IAgentReleaseService
{
    private readonly SemaphoreSlim publishGate = new(1, 1);
    private static string PathToReleases(AgentUpdateOptions o)=>Path.GetFullPath(Path.IsPathRooted(o.ReleasesPath)?o.ReleasesPath:Path.Combine(AppContext.BaseDirectory,o.ReleasesPath));
    public string? LatestPath(){var p=Path.Combine(PathToReleases(options),"latest.json");return File.Exists(p)?p:null;}
    public string? PackagePath(string? version = null)
    {
        if (version is not null)
        {
            if (!Version.TryParse(version, out _)) return null;
            var versioned = Path.Combine(PathToReleases(options), "agent-" + version + ".zip");
            if (File.Exists(versioned)) return versioned;
        }
        var manifest=LatestPath();if(manifest is null)return null;
        AgentRelease? current;
        try{current=JsonSerializer.Deserialize<AgentRelease>(File.ReadAllText(manifest),new JsonSerializerOptions(JsonSerializerDefaults.Web));}
        catch(JsonException){throw new NodeApiException(503,"Манифест обновлений повреждён.");}
        if (current is null || version is not null && current.Version != version) return null;
        var safeName = current.FileName == "agent-" + current.Version + ".zip" && Version.TryParse(current.Version, out _) ? current.FileName : "agent.zip";
        var p=Path.Combine(PathToReleases(options),safeName);return File.Exists(p)?p:null;
    }
    public async Task<AgentRelease> Publish(IFormFile file,string version,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(options.PrivateKeyPath)||!File.Exists(options.PrivateKeyPath))throw new NodeApiException(503,"Нет приватного ключа подписи обновлений.");
        if(!Version.TryParse(version,out var parsed)||parsed is null||file.Length is <100 or >100_000_000)throw new NodeApiException(400,"Недопустимый релиз.");
        await publishGate.WaitAsync(ct);
        try { return await SignAndPublish(file, version, parsed, ct); }
        finally { publishGate.Release(); }
    }

    private async Task<AgentRelease> SignAndPublish(IFormFile file, string version, Version parsed, CancellationToken ct)
    {
        var directory=PathToReleases(options);Directory.CreateDirectory(directory);
        var previous=Path.Combine(directory,"latest.json");
        if(File.Exists(previous))
        {
            try
            {
                var old=JsonSerializer.Deserialize<AgentRelease>(await File.ReadAllTextAsync(previous,ct),new JsonSerializerOptions(JsonSerializerDefaults.Web));
                if(old is not null&&parsed<=Version.Parse(old.Version))throw new NodeApiException(400,"Версия не новее опубликованной.");
            }
            catch(JsonException){throw new NodeApiException(503,"Манифест предыдущего релиза повреждён.");}
        }
        var tmp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
        try
        {
            await using(var stream=File.Create(tmp))await file.CopyToAsync(stream,ct);
            using(var zip=ZipFile.OpenRead(tmp))
            {
                var files = zip.Entries.Select(entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
                if (zip.Entries.Count is <1 or >2000 || zip.Entries.Sum(entry => entry.Length) > 500_000_000 ||
                    !files.IsSupersetOf(new[] { "Api.Agent.dll", "Api.Agent.runtimeconfig.json", "Api.Agent.deps.json" }))
                    throw new NodeApiException(400, "Релиз Agent должен содержать полный publish в корне ZIP.");
                foreach(var entry in zip.Entries)
                {
                    var name=entry.FullName.Replace('\\','/');
                    if(name.StartsWith('/')||name.Split('/').Any(x=>x=="..")||name.Contains(':'))throw new NodeApiException(400,"Небезопасный путь в ZIP.");
                }
            }
            using var rsa=RSA.Create();rsa.ImportFromPem(await File.ReadAllTextAsync(options.PrivateKeyPath,ct));
            string sha;
            await using (var input = File.OpenRead(tmp))
                sha = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)).ToLowerInvariant();
            var release=new AgentRelease(version,sha,file.Length,"agent-"+version+".zip",DateTimeOffset.UtcNow.ToString("O"),"");
            var payload=$"{release.Version}\n{release.Sha256}\n{release.Size}";
            release=release with {Signature=Convert.ToBase64String(rsa.SignData(Encoding.UTF8.GetBytes(payload),HashAlgorithmName.SHA256,RSASignaturePadding.Pss))};
            File.Move(tmp,Path.Combine(directory,release.FileName),true);
            var tmpManifest=Path.Combine(directory,"latest.json.tmp");
            await File.WriteAllTextAsync(tmpManifest,JsonSerializer.Serialize(release,new JsonSerializerOptions(JsonSerializerDefaults.Web)),ct);
            File.Move(tmpManifest,previous,true);
            return release;
        }
        finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
}
public sealed class AgentUpdateWorker(AgentUpdateOptions options, NodeOptions node, IdentityTokenSource tokens,
    AgentUpdateState state, ILogger<AgentUpdateWorker> logger, GuestSessions? guests = null) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!options.Enabled) return;
        while (!ct.IsCancellationRequested)
        {
            try { await CheckAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception error) { state.Error = error.Message; state.Status = "Ошибка проверки"; logger.LogWarning(error, "Agent update check failed"); }
            try { await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(options.PollMinutes, 1, 1440)), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    public async Task CheckAsync(CancellationToken ct)
    {
        if (!await gate.WaitAsync(0, ct)) return;
        try
        {
            state.CheckedAt = DateTimeOffset.UtcNow; state.Error = null;
            var sources = new[] { ("local", node.LocalUrl, options.LocalPublicKeyPath), ("global", node.GlobalUrl, options.PublicKeyPath) };
            var choices = new List<(AgentRelease Release, string Source, Uri Endpoint)>();
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await tokens.GetAsync(ct));
            foreach (var (source, url, key) in sources)
            {
                if (!Uri.TryCreate(url?.TrimEnd('/') + "/", UriKind.Absolute, out var endpoint) || endpoint is null ||
                    !File.Exists(key) || !(endpoint.Scheme == "https" || endpoint.Scheme == "http" &&
                    (endpoint.IsLoopback || node.AllowInsecureTestHttp && (source == "local" || System.Net.IPAddress.TryParse(endpoint.Host, out var address) && ClubLan.PrivateAddress(address))))) continue;
                var pem = await File.ReadAllTextAsync(key, ct);
                if (string.IsNullOrWhiteSpace(pem)) continue;
                try
                {
                    using var response = await client.GetAsync(new Uri(endpoint, "api/v1/updates/agent/latest"), ct);
                    if (!response.IsSuccessStatusCode) continue;
                    var seen = await response.Content.ReadFromJsonAsync<AgentRelease>(ct);
                    if (seen is not null && AgentReleaseEndpoints.Verify(seen, pem)) choices.Add((seen, source, endpoint));
                }
                catch (Exception error) when (error is HttpRequestException or JsonException || error is OperationCanceledException && !ct.IsCancellationRequested)
                { logger.LogDebug("Release source {Source} is unavailable", source); }
            }
            var best = choices.OrderByDescending(value => Version.Parse(value.Release.Version)).FirstOrDefault();
            if (best.Release is null) { state.Status = "Ожидаем подписанный релиз Local или Global"; return; }
            var release = best.Release;
            state.Version = release.Version;
            if (Version.Parse(release.Version) <= Version.Parse(options.CurrentVersion))
            { state.Status = "Установлена актуальная версия"; return; }
            if (Busy()) { state.Status = "Обновление ждёт завершения активного сеанса"; return; }
            var load = await HostLoad.High(ct);
            if (load) { state.Status = "Обновление ждёт снижения нагрузки"; return; }
            var path = Path.GetFullPath(Path.IsPathRooted(options.StagingPath) ? options.StagingPath : Path.Combine(AppContext.BaseDirectory, options.StagingPath));
            Directory.CreateDirectory(path);
            var staged = Path.Combine(path, "agent-" + release.Version + ".zip");
            if (!await Verified(staged, release, ct))
            {
                var temp = staged + ".download";
                try
                {
                    using var response = await client.GetAsync(new Uri(best.Endpoint, "api/v1/updates/agent/package?version=" +
                        Uri.EscapeDataString(release.Version)), HttpCompletionOption.ResponseHeadersRead, ct);
                    response.EnsureSuccessStatusCode();
                    await using (var input = await response.Content.ReadAsStreamAsync(ct))
                    await using (var output = File.Create(temp))
                    {
                        var buffer = new byte[65_536];
                        int read; long total = 0;
                        while ((read = await input.ReadAsync(buffer, ct)) != 0)
                        {
                            total += read;
                            if (total > release.Size) throw new InvalidDataException("Превышен объявленный размер релиза.");
                            await output.WriteAsync(buffer.AsMemory(0, read), ct);
                        }
                    }
                    if (!await Verified(temp, release, ct)) throw new CryptographicException("Не совпала контрольная сумма релиза.");
                    File.Move(temp, staged, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            state.Status = "Подписанный пакет загружен";
            if (!options.AutoInstall || !node.Managed || Busy()) return;
            var marker = Path.Combine(path, "pending.json");
            await File.WriteAllTextAsync(marker + ".tmp", JsonSerializer.Serialize(new PendingAgentRelease(release, best.Source), Json), ct);
            File.Move(marker + ".tmp", marker, true);
            state.Status = "Релиз передан AuditDesktop для установки";
        }
        finally { gate.Release(); }
    }

    private bool Busy() => guests?.Active == true || AutomationPlaybackCommand.Running;
    private static async Task<bool> Verified(string file, AgentRelease release, CancellationToken ct)
    {
        if (!File.Exists(file) || new FileInfo(file).Length != release.Size) return false;
        await using var input = File.OpenRead(file);
        return string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(input, ct)), release.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static class HostLoad
    {
        public static async Task<bool> High(CancellationToken ct)
        {
            var start = Read();
            if (start.Total == 0) return false;
            await Task.Delay(700, ct);
            var end = Read();
            return end.Total > start.Total && 1.0 - (end.Idle - start.Idle) / (double)(end.Total - start.Total) > .6;
        }

        private static (long Total, long Idle) Read()
        {
            if (OperatingSystem.IsWindows()) return Native(out var idle, out var kernel, out var user) ? (kernel + user, idle) : default;
            if (!OperatingSystem.IsLinux()) return default;
            try
            {
                var counters = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Skip(1).Take(8).Select(long.Parse).ToArray();
                return counters.Length >= 5 ? (counters.Sum(), counters[3] + counters[4]) : default;
            }
            catch (IOException) { return default; }
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "GetSystemTimes")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool Native(out long idle, out long kernel, out long user);
    }
}
