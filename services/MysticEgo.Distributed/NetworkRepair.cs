using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MysticEgo.Distributed;

// Native Windows tools are wrapped by a constrained, authenticated .NET coordinator.
// No arbitrary command, URL or filesystem path is accepted from an HTTP request.
public sealed class NetworkRepairOptions
{
    public bool Enabled { get; set; } = false;
    public string Root { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MysticEgo", "NetworkRepair");
    public string GitHubOwner { get; set; } = "Flowseal";
    public string GitHubRepository { get; set; } = "zapret-discord-youtube";
    public string? PinnedReleaseSha256 { get; set; }
    public string[] ProbeHosts { get; set; } = ["youtube.com", "discord.com", "github.com"];
    public int ProbeTimeoutSeconds { get; set; } = 6;
    public int StrategySettleSeconds { get; set; } = 3;
    public int MaxStrategies { get; set; } = 32;
    // Potentially disruptive file and system changes are disabled unless an administrator opts in.
    public bool AllowSystemChanges { get; set; } = false;
    public bool AllowPackageChanges { get; set; } = false;
    public int SignalRgbMemoryLimitMb { get; set; } = 3072;
}

public sealed record RepairOperation(string Action, string? Strategy = null, string? Mode = null, bool Confirmed = false, string? Value = null, string[]? Domains = null);
public sealed record RepairResult(bool Success, string Message, object? Details = null);

public interface INetworkRepairService
{
    object Status();
    Task<RepairResult> ExecuteAsync(RepairOperation request, CancellationToken ct);
}

public sealed partial class NetworkRepairService : INetworkRepairService
{
    private readonly NetworkRepairOptions _options;
    private readonly ILogger<NetworkRepairService> _logger;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1,1);
    private Process? _ownedStrategy;
    private readonly HashSet<int> _ownedWinwsIds = [];
    private bool IsActive => _ownedWinwsIds.Any(id=>IsRunning(id)) || _ownedStrategy is {HasExited:false};
    private static bool IsRunning(int id){try{using var p=Process.GetProcessById(id);return !p.HasExited;}catch{return false;}}
    private string? _activeStrategy;
    private string? _lastError;
    private DateTimeOffset? _lastUpdated;
    private Dictionary<string,int> _scores = new(StringComparer.OrdinalIgnoreCase);
    private string Installed => Path.Combine(_options.Root,"zapret");
    private string WarpCli => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Cloudflare","Cloudflare WARP","warp-cli.exe");
    public NetworkRepairService(NetworkRepairOptions options, ILogger<NetworkRepairService> logger, IHttpClientFactory factory)
    {
        _options=options;_logger=logger;_http=factory.CreateClient("network-repair");
        // Storage initialization is deliberately deferred; a broken optional module
        // must never prevent Agent/AuditOrchestra from starting.
    }
    private static bool Windows => OperatingSystem.IsWindows();
    private bool Supported => Windows && _options.Enabled;
    private static bool SafeName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length<100 && value.All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '.');
    private string[] Strategies()
    {
        try{return Directory.Exists(Installed)? Directory.EnumerateFiles(Installed,"general*.bat",SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName).Where(s=>s is not null&&SafeName(s)).Cast<string>().OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray():[];}
        catch(Exception e){_logger.LogWarning(e,"Unable to enumerate strategies");return [];}
    }
    public object Status()
    {
        object extras;
        try{extras=ExtendedStatus();}
        catch(Exception e){_logger.LogWarning(e,"Optional network repair status unavailable");extras=new {error="Optional status unavailable"};}
        return new {enabled=_options.Enabled,platform=OperatingSystem.IsWindows()?"windows":"unsupported",
            installed=File.Exists(Path.Combine(Installed,"general.bat")),active=IsActive,
            strategy=_activeStrategy,warpInstalled=File.Exists(WarpCli),lastUpdated=_lastUpdated,
            lastError=_lastError,scores=_scores,strategies=Strategies(),extras};
    }
    private static bool IsAdministrator()
    {
        if(!Windows)return false;
        using var id=System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(id).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    private static async Task<(int Code,string Output)> RunAsync(string file,string args,CancellationToken ct,int timeout=30)
    {
        using var proc = new Process{StartInfo=new ProcessStartInfo(file,args){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true}};
        if(!proc.Start())throw new IOException("Process start failed");
        using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(timeout));
        try
        {
            var stdout=proc.StandardOutput.ReadToEndAsync(limit.Token);
            var stderr=proc.StandardError.ReadToEndAsync(limit.Token);
            await proc.WaitForExitAsync(limit.Token);
            var combined=((await stdout)+"\n"+(await stderr)).Trim();
            return (proc.ExitCode,combined.Length>3000?combined[..3000]:combined);
        }
        catch(OperationCanceledException){if(!proc.HasExited)proc.Kill(entireProcessTree:true);throw;}
    }
    private async Task StopOwnedAsync(CancellationToken ct)
    {
        if(_ownedStrategy is {HasExited:false} process){try{process.Kill(entireProcessTree:true);await process.WaitForExitAsync(ct);}catch(Exception e){_logger.LogWarning(e,"Failed to stop owned strategy tree");}}
        foreach(var id in _ownedWinwsIds.ToArray())
        {
            try{using var p=Process.GetProcessById(id);if(!p.HasExited && p.ProcessName.Equals("winws",StringComparison.OrdinalIgnoreCase))p.Kill(entireProcessTree:true);}
            catch(Exception e){_logger.LogDebug(e,"Process already exited");}
        }
        _ownedWinwsIds.Clear();_ownedStrategy?.Dispose();_ownedStrategy=null;_activeStrategy=null;
    }
    private async Task<RepairResult> StartStrategyAsync(string? strategy,CancellationToken ct)
    {
        var choices=Strategies();strategy ??= "general.bat";
        if(!SafeName(strategy)||!choices.Contains(strategy,StringComparer.OrdinalIgnoreCase))return new(false,"Strategy is not installed");
        if(!File.Exists(Path.Combine(Installed,"bin","winws.exe")))return new(false,"winws.exe missing in package");
        await StopOwnedAsync(ct);
        var existing=Process.GetProcessesByName("winws").Select(p=>{var id=p.Id;p.Dispose();return id;}).ToHashSet();
        if(existing.Count>0)return new(false,"A winws instance is already running; refuse to interfere with another process");
        var batch=Path.Combine(Installed,strategy);
        // .bat is supplied by the verified upstream package; we never evaluate user-provided shell text.
        var psi=new ProcessStartInfo("cmd.exe") {UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Installed};
        psi.ArgumentList.Add("/d");psi.ArgumentList.Add("/c");psi.ArgumentList.Add(batch);
        var process=Process.Start(psi);
        if(process is null)return new(false,"Failed to start strategy");
        _ownedStrategy=process;_activeStrategy=strategy;
        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1,_options.StrategySettleSeconds)),ct);
        foreach(var p in Process.GetProcessesByName("winws"))
        {if(!existing.Contains(p.Id))_ownedWinwsIds.Add(p.Id);p.Dispose();}
        if(_ownedWinwsIds.Count==0){await StopOwnedAsync(ct);return new(false,"winws did not start; see service log");}
        return new(true,"Strategy started",new {strategy});
    }
    private async Task<RepairResult> UpdateZapretAsync(CancellationToken ct)
    {
        // Prevent self-replacement while the strategy is running.
        if(IsActive)return new(false,"Stop Zapret before updating");
        // Fetch upstream metadata from a fixed, administrator-configured GitHub repository.
        var uri=$"https://api.github.com/repos/{_options.GitHubOwner}/{_options.GitHubRepository}/releases/latest";
        using var meta=await _http.GetAsync(uri,ct);meta.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await meta.Content.ReadAsStreamAsync(ct));
        var tag=json.RootElement.GetProperty("tag_name").GetString() ?? "unknown";
        var asset=json.RootElement.GetProperty("assets").EnumerateArray()
            .FirstOrDefault(x=>x.GetProperty("name").GetString()?.EndsWith(".zip",StringComparison.OrdinalIgnoreCase)==true);
        if(asset.ValueKind==JsonValueKind.Undefined)return new(false,"No ZIP asset in release");
        var assetUrl=asset.GetProperty("browser_download_url").GetString() ?? "";
        if(!Uri.TryCreate(assetUrl,UriKind.Absolute,out var url)||url.Scheme!="https"||
            !(url.Host.Equals("github.com",StringComparison.OrdinalIgnoreCase)||url.Host.Equals("objects.githubusercontent.com",StringComparison.OrdinalIgnoreCase)))
            return new(false,"Release asset host not approved");
        var digest=asset.TryGetProperty("digest",out var d)?d.GetString():null;
        string? expected=_options.PinnedReleaseSha256;
        if(string.IsNullOrWhiteSpace(expected)&&digest?.StartsWith("sha256:",StringComparison.OrdinalIgnoreCase)==true)expected=digest[7..];
        if(expected is null || expected.Length!=64 || !expected.All(Uri.IsHexDigit))return new(false,"No verified SHA-256 digest; configure PinnedReleaseSha256");
        var zipFile=Path.Combine(_options.Root,Guid.NewGuid()+".zip");
        var stage=Path.Combine(_options.Root,"stage-"+Guid.NewGuid().ToString("N"));
        var old=Path.Combine(_options.Root,"rollback-"+Guid.NewGuid().ToString("N"));
        try
        {
            using(var response=await _http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,ct))
            {
                response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength is > 250_000_000)return new(false,"Release too large");
                await using var output=File.Create(zipFile);
                await using var input=await response.Content.ReadAsStreamAsync(ct);
                var buffer=new byte[81920];long total=0;int read;
                while((read=await input.ReadAsync(buffer,ct))>0)
                {total+=read;if(total>250_000_000)throw new InvalidDataException("Release size limit exceeded");await output.WriteAsync(buffer.AsMemory(0,read),ct);}
            }
            await using(var f=File.OpenRead(zipFile))
            {var actual=Convert.ToHexString(await SHA256.HashDataAsync(f,ct));if(!actual.Equals(expected,StringComparison.OrdinalIgnoreCase))return new(false,"Checksum mismatch");}
            Directory.CreateDirectory(stage);
            using(var archive=ZipFile.OpenRead(zipFile))
            {
                long expanded=0;
                foreach(var entry in archive.Entries)
                {
                    expanded+=entry.Length;if(expanded>1_000_000_000)throw new InvalidDataException("Expanded size limit exceeded");
                    // Reject Unix symlinks inside archives to avoid writing outside staging.
                    if(((entry.ExternalAttributes >> 16) & 0xF000)==0xA000)throw new InvalidDataException("Archive symlink is not allowed");
                    var dest=Path.GetFullPath(Path.Combine(stage,entry.FullName));
                    if(!dest.StartsWith(Path.GetFullPath(stage)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Unsafe archive path");
                    if(entry.FullName.EndsWith('/')){Directory.CreateDirectory(dest);continue;}
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);entry.ExtractToFile(dest);
                }
            }
            if(!File.Exists(Path.Combine(stage,"general.bat")))
            {
                var matches=Directory.GetFiles(stage,"general.bat",SearchOption.AllDirectories);
                if(matches.Length!=1)return new(false,"Unexpected release layout");
                var sub=Path.GetDirectoryName(matches[0])!;
                var normalized=stage+"-flat";Directory.Move(sub,normalized);Directory.Delete(stage,true);Directory.Move(normalized,stage);
            }
            if(!File.Exists(Path.Combine(stage,"bin","winws.exe")))return new(false,"Missing executable in package");
            if(Directory.Exists(Installed))Directory.Move(Installed,old);
            try{Directory.Move(stage,Installed);}catch{if(Directory.Exists(old))Directory.Move(old,Installed);throw;}
            if(Directory.Exists(old))Directory.Delete(old,true);
            await File.WriteAllTextAsync(Path.Combine(_options.Root,"release.txt"),tag,ct);
            _lastUpdated=DateTimeOffset.UtcNow;_scores.Clear();
            return new(true,"Zapret updated",new {version=tag,checksum=expected});
        }
        finally{if(File.Exists(zipFile))File.Delete(zipFile);if(Directory.Exists(stage))Directory.Delete(stage,true);}
    }
    private async Task<RepairResult> ProbeAsync(CancellationToken ct)
    {
        var report=new Dictionary<string,bool>(StringComparer.OrdinalIgnoreCase);
        foreach(var hostname in (LoadDomains(TestDomainsFile).Length>0?LoadDomains(TestDomainsFile):_options.ProbeHosts).Take(20))
        {
            if(!Uri.CheckHostName(hostname).Equals(UriHostNameType.Dns))continue;
            try{using var token=CancellationTokenSource.CreateLinkedTokenSource(ct);token.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.ProbeTimeoutSeconds,2,20)));
                using var resp=await _http.GetAsync("https://"+hostname,HttpCompletionOption.ResponseHeadersRead,token.Token);
                report[hostname]=resp.IsSuccessStatusCode;
            }catch(Exception){report[hostname]=false;}
        }
        return new(true,"Connectivity probes completed",report);
    }
    private async Task<RepairResult> TestStrategiesAsync(CancellationToken ct)
    {
        if(IsActive)return new(false,"Stop current strategy before benchmark");
        var choices=Strategies().Take(Math.Clamp(_options.MaxStrategies,1,100)).ToArray();
        if(choices.Length==0)return new(false,"No strategies available");
        var scores=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
        var map=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach(var name in choices)
            {
                ct.ThrowIfCancellationRequested();
                var run=await StartStrategyAsync(name,ct);
                if(!run.Success){scores[name]=-1;continue;}
                var probe=await ProbeAsync(ct);
                if(probe.Details is Dictionary<string,bool> values)
                {
                    scores[name]=values.Count(v=>v.Value);
                    foreach(var (domain,ok) in values)
                        if(ok&&!map.ContainsKey(domain))map[domain]=name;
                }
                else scores[name]=0;
                await StopOwnedAsync(ct);
            }
        }
        finally{await StopOwnedAsync(CancellationToken.None);}
        _scores=scores;
        await SaveAtomicAsync(StrategyMapFile,string.Join("\r\n",map.Select(kv=>kv.Key+"="+kv.Value))+"\r\n",ct);
        var best=scores.Where(x=>x.Value>=0).OrderByDescending(x=>x.Value).ThenBy(x=>x.Key,StringComparer.OrdinalIgnoreCase).FirstOrDefault();
        if(best.Key is null)return new(false,"No working strategy",scores);
        await File.WriteAllTextAsync(Path.Combine(_options.Root,"best_strategy.txt"),best.Key,ct);
        return new(true,"Strategy benchmark completed",new {best=best.Key,scores});
    }
    private async Task<RepairResult> WarpAsync(string mode,CancellationToken ct)
    {
        if(!File.Exists(WarpCli))return new(false,"WARP is not installed. Use warp-install first");
        // Manager.bat combines Zapret and WARP for certain game profiles.
        // Operator explicitly confirms the combined state; independent recovery remains possible.
        if(mode=="off")
        {var r=await RunAsync(WarpCli,"disconnect",ct);return new(r.Code==0,"WARP disconnected",r.Output);}
        if(mode is not("warp" or "proxy"))return new(false,"Unsupported WARP mode");
        var stop=await RunAsync(WarpCli,"disconnect",ct);
        var set=await RunAsync(WarpCli,"mode "+mode,ct);
        if(set.Code!=0)return new(false,"Cannot set WARP mode",set.Output);
        var start=await RunAsync(WarpCli,"connect",ct);
        return new(start.Code==0,"WARP command completed",start.Output);
    }
    private async Task<RepairResult> InstallWarpAsync(CancellationToken ct)
    {
        if(File.Exists(WarpCli))return new(true,"WARP already installed");
        // Only trusted package-manager identifier; avoid silent execution of downloaded MSI.
        var r=await RunAsync("winget.exe","install --id Cloudflare.Warp -e --silent --accept-package-agreements --accept-source-agreements",ct,360);
        return new(r.Code==0&&File.Exists(WarpCli),"WARP installation via winget completed",r.Output);
    }
    private async Task<RepairResult> FixAsync(string mode,CancellationToken ct)
    {
        // Cross-game non-destructive diagnostics and maintenance only. Hosts-file edits,
        // permanent DNS changes and third-party reinstallations are deliberately excluded.
        return mode switch
        {
            "flush-dns"=> await ExecFixAsync("ipconfig.exe","/flushdns",ct),
            "winsock-diagnostics"=>await ExecFixAsync("netsh.exe","winsock show catalog",ct),
            "network-diagnostics"=>await ProbeAsync(ct),
            _=>new(false,"Unknown or unsupported repair")
        };
    }
    private static async Task<RepairResult> ExecFixAsync(string tool,string args,CancellationToken ct)
    {var r=await RunAsync(tool,args,ct);return new(r.Code==0,"Diagnostic/repair command finished",r.Output);}
    public async Task<RepairResult> ExecuteAsync(RepairOperation request,CancellationToken ct)
    {
        if(!Supported)return new(false,"Network repair is disabled or unsupported on this operating system");
        if(!request.Confirmed)return new(false,"Explicit confirmation required");
        if(!IsAdministrator())return new(false,"A locally installed elevated Windows service is required");
        if(!await _gate.WaitAsync(TimeSpan.FromSeconds(1),ct))return new(false,"Another operation is running");
        try
        {
            Directory.CreateDirectory(_options.Root);
            var response=request.Action switch
            {
                "update-zapret"=>await UpdateZapretAsync(ct),
                "start-zapret"=>await StartStrategyAsync(request.Strategy ?? (File.Exists(Path.Combine(_options.Root,"best_strategy.txt"))? (await File.ReadAllTextAsync(Path.Combine(_options.Root,"best_strategy.txt"),ct)).Trim():"general.bat"),ct),
                "stop-zapret"=>await StopAndRespond(ct),
                "test-strategies"=>await TestStrategiesAsync(ct),
                "probe"=>await ProbeAsync(ct),
                "warp-install"=>await InstallWarpAsync(ct),
                "warp-on"=>await WarpAsync(request.Mode??"warp",ct),
                "warp-off"=>await WarpAsync("off",ct),
                "flush-dns" or "winsock-diagnostics" or "network-diagnostics"=>await FixAsync(request.Action,ct),
                _ => await ExecuteExtendedAsync(request,ct)
                // The legacy manager's remaining functions are implemented in the companion partial.

            };
            if(!response.Success)_lastError=response.Message;
            else _lastError=null;
            _logger.LogInformation("Network repair {Action} success={Success}",request.Action,response.Success);
            return response;
        }
        catch(Exception e){_lastError=e.Message;_logger.LogError(e,"Network repair failed: {Action}",request.Action);return new(false,"Operation failed: "+e.Message);}
        finally{_gate.Release();}
    }
    private async Task<RepairResult> StopAndRespond(CancellationToken ct){await StopOwnedAsync(ct);return new(true,"Owned Zapret process tree stopped");}
}
public static class NetworkRepairEndpoints
{
    public static IServiceCollection AddNetworkRepair(this IServiceCollection services,IConfiguration configuration)
    {
        var o=new NetworkRepairOptions();configuration.GetSection("NetworkRepair").Bind(o);
        services.AddSingleton(o);
        services.AddHttpClient("network-repair",c=>{c.Timeout=TimeSpan.FromMinutes(10);c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MysticEgo","1.0"));});
        services.AddSingleton<NetworkRepairService>();
        services.AddSingleton<INetworkRepairService>(sp=>sp.GetRequiredService<NetworkRepairService>());
        services.AddHostedService<SignalRgbWatchdogService>();return services;
    }

}
