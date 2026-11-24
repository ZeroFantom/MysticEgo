using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MysticEgo.Distributed;

// A managed implementation of the remaining Manager.bat menu operations.
// Only fixed, named operations are exposed. No arbitrary shell commands or URLs are accepted.
public sealed partial class NetworkRepairService
{
    private string Lists => Path.Combine(Installed, "lists");
    private string UserDomains => Path.Combine(Lists, "list-general-user.txt");
    private string DnsSnapshot => Path.Combine(_options.Root, "dns-backup.json");
    private string HostsBackup => Path.Combine(_options.Root, "roblox-hosts-backup.txt");
    private string Quarantine => Path.Combine(_options.Root, "pubg-video-quarantine");
    private string QuarantineIndex => Path.Combine(Quarantine, "manifest.json");
    private string TestDomainsFile => Path.Combine(_options.Root, "test_domains.txt");
    private string StrategyMapFile => Path.Combine(_options.Root, "strategy_map.txt");
    private string WatchdogMarker => Path.Combine(_options.Root, "signalrgb-watchdog.enabled");
    private const string TelegramProxyUri = "tg://socks?server=127.0.0.1&port=40000";
    private static readonly string[] BaselineDomains = [
        "workers.dev", "pages.dev", "telegram.org", "telegram.me", "t.me", "tdesktop.com", "telegra.ph",
        "discord.com", "discord.gg", "discordapp.com", "discordapp.net", "discord.media", "discordcdn.com",
        "youtu.be", "ytimg.com", "googlevideo.com", "youtubei.googleapis.com",
        "photonengine.io", "ns.photonengine.io", "ns-eu.photonengine.io", "ns-us.photonengine.io",
        "ns-jp.photonengine.io", "photonmaster.online-fix.me",
        "playbattlegrounds.com", "pubg.com", "vnggames.com", "proximabeta.com", "epicgames.com",
        "fortnite.com", "accounts.epicgames.com", "ol.epicgames.com", "unrealengine.com",
        "cdn1.unrealengine.com", "cdn2.unrealengine.com", "epicgames-download1.akamaized.net", "download.epicgames.com"
    ];
    private static readonly string[] PubgDomains = [
        "playbattlegrounds.com", "pubg.com", "vnggames.com", "proximabeta.com", "epicgames.com",
        "fortnite.com", "accounts.epicgames.com", "ol.epicgames.com", "unrealengine.com",
        "cdn1.unrealengine.com", "cdn2.unrealengine.com", "epicgames-download1.akamaized.net", "download.epicgames.com"
    ];
    private static readonly string[] PhasmophobiaDomains = [
        "photonengine.io", "ns.photonengine.io", "ns-eu.photonengine.io", "ns-us.photonengine.io",
        "ns-jp.photonengine.io", "photonmaster.online-fix.me"
    ];
    private static readonly Dictionary<string, string> Packages = new(StringComparer.OrdinalIgnoreCase)
    {
        ["signalrgb"] = "WhirlwindFX.SignalRgb",
        ["msi-center"] = "MSI.MSICenter",
        ["ghub"] = "Logitech.GHUB",
        ["fury"] = "Kingston.FuryCTRL",
        ["steam"] = "Valve.Steam",
        ["epic"] = "EpicGames.EpicGamesLauncher",
        ["battlenet"] = "Blizzard.BattleNet",
        ["roblox"] = "Roblox.Roblox"
    };
    private static readonly Dictionary<string,string[]> PackageExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        ["signalrgb"] = ["SignalRgbLauncher.exe", "SignalRgb.exe"],
        ["msi-center"] = ["MSI Center.exe", "MSICenter.exe"],
        ["ghub"] = ["lghub.exe"],
        ["fury"] = ["FuryCtrl.exe", "FuryControl.exe"],
        ["steam"] = ["steam.exe"],
        ["epic"] = ["EpicGamesLauncher.exe"],
        ["battlenet"] = ["Battle.net.exe", "Battle.net Launcher.exe"],
        ["roblox"] = ["RobloxPlayerBeta.exe"]
    };
    private static readonly Dictionary<string,string[]> PackageCandidates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["signalrgb"] = [@"%LOCALAPPDATA%\VortxEngine\SignalRgbLauncher.exe"],
        ["msi-center"] = [@"%PROGRAMFILES%\MSI\MSI Center\MSI Center.exe"],
        ["ghub"] = [@"%PROGRAMFILES%\LGHUB\lghub.exe"],
        ["fury"] = [@"%PROGRAMFILES%\Kingston\FURY CTRL\FuryCtrl.exe"],
        ["steam"] = [@"%PROGRAMFILES(X86)%\Steam\steam.exe", @"%PROGRAMFILES%\Steam\steam.exe"],
        ["epic"] = [@"%PROGRAMFILES%\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe",
                    @"%PROGRAMFILES(X86)%\Epic Games\Launcher\Portal\Binaries\Win32\EpicGamesLauncher.exe"],
        ["roblox"] = [@"%LOCALAPPDATA%\Roblox\Versions\RobloxPlayerBeta.exe"],
        ["battlenet"] = [@"%PROGRAMFILES(X86)%\Battle.net\Battle.net Launcher.exe",
                      @"%PROGRAMFILES%\Battle.net\Battle.net Launcher.exe",
                      @"%PROGRAMFILES(X86)%\Battle.net\Battle.net.exe"]
    };
    private static string ExpandPath(string path)
    {
        // ExpandEnvironmentVariables on Windows does not always expand PROGRAMFILES(X86) for system services.
        return Environment.ExpandEnvironmentVariables(path);
    }
    private static string? InstalledExecutable(string id)
    {
        if (id=="roblox")
        {
            var root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Roblox","Versions");
            if(Directory.Exists(root))
            {
                var matches=Directory.EnumerateFiles(root,"RobloxPlayerBeta.exe",SearchOption.AllDirectories).FirstOrDefault();
                if(matches is not null)return matches;
            }
        }
        if (!PackageCandidates.TryGetValue(id,out var paths))return null;
        return paths.Select(ExpandPath).FirstOrDefault(File.Exists);
    }
    private object ExtendedStatus() => new {
        packages=Packages.Keys.ToDictionary(k=>k,k=>new {installed=InstalledExecutable(k)!=null, packageId=Packages[k]}),
        systemChangesAllowed=_options.AllowSystemChanges,packageChangesAllowed=_options.AllowPackageChanges,
        watchdogEnabled=File.Exists(WatchdogMarker),
        backupAvailable=File.Exists(DnsSnapshot),robloxHostsBackup=File.Exists(HostsBackup),
        pubgQuarantine=File.Exists(QuarantineIndex),
        testDomains=LoadDomains(TestDomainsFile),
        strategyMap=File.Exists(StrategyMapFile)?ReadShort(StrategyMapFile):""
    };
    private static string ReadShort(string file) => File.ReadAllText(file).Length>20000?File.ReadAllText(file)[..20000]:File.ReadAllText(file);
    private static bool DomainAllowed(string s) => s.Length is > 3 and < 254 &&
        Uri.CheckHostName(s)==UriHostNameType.Dns &&
        s.All(c=>char.IsAsciiLetterOrDigit(c)||c is '.' or '-');
    private static string[] LoadDomains(string file) => !File.Exists(file)?[]:File.ReadLines(file)
        .Select(x=>x.Trim()).Where(x=>!x.StartsWith('#')&&!x.StartsWith(';')&&DomainAllowed(x))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToArray();
    private static async Task SaveAtomicAsync(string path,string text,CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var stage=path+".tmp-"+Guid.NewGuid().ToString("N");
        try
        {
            await File.WriteAllTextAsync(stage,text,Encoding.UTF8,ct);
            File.Move(stage,path,true);
        }
        finally{if(File.Exists(stage))File.Delete(stage);}
    }
    private static async Task<(int Code,string Output)> RunArgsAsync(string executable,IEnumerable<string> args,CancellationToken ct,int timeout=180)
    {
        using var process=new Process { StartInfo = new ProcessStartInfo(executable){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true } };
        foreach(var arg in args)process.StartInfo.ArgumentList.Add(arg);
        if(!process.Start())return (-1,"Process could not start");
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(TimeSpan.FromSeconds(timeout));
        try
        {
            var stdout=process.StandardOutput.ReadToEndAsync(linked.Token);
            var stderr=process.StandardError.ReadToEndAsync(linked.Token);
            await process.WaitForExitAsync(linked.Token);
            var merged=(await stdout)+"\n"+(await stderr);
            return (process.ExitCode,merged.Length>5000?merged[..5000]:merged);
        }
        catch(OperationCanceledException){if(!process.HasExited)process.Kill(true);throw;}
    }
    private async Task<RepairResult> PackagesAsync(string action,string id,CancellationToken ct)
    {
        if(!_options.AllowPackageChanges)return new(false,"Package installation/uninstallation disabled by administrator");
        if(!Packages.TryGetValue(id,out var package))return new(false,"Unknown application");
        var command=action switch {"install"=>"install","update"=>"upgrade","uninstall"=>"uninstall",_=>""};
        if(command.Length==0)return new(false,"Package operation not allowed");
        var args=new List<string>{command,"--id",package,"-e","--silent","--disable-interactivity"};
        if(command!="uninstall")args.AddRange(["--accept-package-agreements","--accept-source-agreements"]);
        var result=await RunArgsAsync("winget.exe",args,ct,600);
        if(result.Code==0&&command=="uninstall")
        {
            // Remove only the autostart entry created by MysticEgo; never alter vendor entries.
            using var run=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",writable:true);
            run?.DeleteValue("MysticEgo_"+id.Replace('-','_'),false);
            if(id=="signalrgb"&&File.Exists(WatchdogMarker))File.Delete(WatchdogMarker);
        }
        return new(result.Code==0,$"{id}: {command} finished with exit code {result.Code}",result.Output);
    }
    private async Task<RepairResult> LaunchAppAsync(string id,CancellationToken ct)
    {
        await Task.CompletedTask;
        var exe=InstalledExecutable(id);
        if(exe==null)return new(false,$"{id} is not installed, or is in a custom path");
        // A Windows service (Session 0) cannot safely launch an application on a user's desktop.
        // In a non-service interactive host, launch is allowed for this vetted, installed executable.
        // Otherwise return the path for a trusted foreground companion to launch it.
        if(Environment.UserInteractive && Process.GetCurrentProcess().SessionId!=0)
        {
            using var started=Process.Start(new ProcessStartInfo(exe){UseShellExecute=true,WorkingDirectory=Path.GetDirectoryName(exe)!});
            return new(started is not null,started is null?"Could not launch application":"Application launched in interactive session",new{application=id,launchPath=exe});
        }
        return new(true,"Interactive desktop action required; service did not start the application",new {application=id,launchPath=exe,requiresInteractiveSession=true});
    }
    private async Task<RepairResult> PackageBatchAsync(CancellationToken ct)
    {
        var results=new List<object>();
        foreach(var id in new[]{"steam","epic","battlenet"})
        {
            ct.ThrowIfCancellationRequested();
            var r=await PackagesAsync(InstalledExecutable(id)==null?"install":"update",id,ct);
            results.Add(new {application=id,r.Success,r.Message,r.Details});
        }
        return new(true,"Launcher maintenance attempted; inspect per-launcher outcomes",results);
    }
    private async Task<RepairResult> SetAutostartAsync(string id,bool enabled,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(!Packages.ContainsKey(id))return new(false,"Unknown application");
        // Changes HKLM (all users); it does not accidentally edit a Windows Service user's HKCU.
        await Task.CompletedTask;
        using var run=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",writable:true);
        if(run is null)return new(false,"Windows Run registry key unavailable");
        var name="MysticEgo_"+id.Replace('-','_');
        if(!enabled){run.DeleteValue(name,false);return new(true,"Autostart disabled");}
        var exe=InstalledExecutable(id);
        if(exe is null)return new(false,"Application executable not found");
        run.SetValue(name,"\""+exe+"\"",RegistryValueKind.String);
        return new(true,"Autostart enabled for all users");
    }
    private async Task<RepairResult> SetWatchdogAsync(bool enabled,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(enabled)await SaveAtomicAsync(WatchdogMarker,"enabled",ct);
        else if(File.Exists(WatchdogMarker))File.Delete(WatchdogMarker);
        return new(true,enabled?"SignalRGB watchdog enabled":"SignalRGB watchdog disabled");
    }
    private async Task<RepairResult> AddDomainsAsync(IEnumerable<string> domains,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(!Directory.Exists(Lists))return new(false,"Zapret lists directory not installed");
        var all=LoadDomains(UserDomains).Concat(domains.Select(x=>x.Trim().ToLowerInvariant())).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(all.Length>5000||all.Any(x=>!DomainAllowed(x)))return new(false,"Invalid domain list");
        await SaveAtomicAsync(UserDomains,string.Join("\r\n",all)+"\r\n",ct);
        return new(true,"Zapret domain list updated",new {count=all.Length});
    }
    private async Task<RepairResult> RefreshListsAsync(CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        var baseResult=await AddDomainsAsync(BaselineDomains,ct);
        if(!baseResult.Success)return baseResult;
        var entries=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var feeds=new[]{"https://www.cloudflare.com/ips-v4","https://www.cloudflare.com/ips-v6"};
        var failures=new List<string>();
        foreach(var url in feeds)
        {
            try
            {
                using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
                linked.CancelAfter(TimeSpan.FromSeconds(25));
                var raw=await _http.GetStringAsync(url,linked.Token);
                foreach(var line in raw.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
                    if(ValidCidr(line))entries.Add(line);
            }
            catch(Exception e){failures.Add(url+": "+e.GetType().Name);}
        }
        try
        {
            using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct);
            linked.CancelAfter(TimeSpan.FromSeconds(35));
            using var stream=await _http.GetStreamAsync("https://www.gstatic.com/ipranges/goog.json",linked.Token);
            using var json=await JsonDocument.ParseAsync(stream,cancellationToken:linked.Token);
            foreach(var prefix in json.RootElement.GetProperty("prefixes").EnumerateArray())
            {
                if(prefix.TryGetProperty("ipv4Prefix",out var ipv4)&&ValidCidr(ipv4.GetString()??""))entries.Add(ipv4.GetString()!);
                if(prefix.TryGetProperty("ipv6Prefix",out var ipv6)&&ValidCidr(ipv6.GetString()??""))entries.Add(ipv6.GetString()!);
            }
        }
        catch(Exception e){failures.Add("Google IP ranges: "+e.GetType().Name);}
        if(entries.Count>0)await SaveAtomicAsync(Path.Combine(Lists,"ipset-user.txt"),string.Join("\r\n",entries.OrderBy(x=>x))+"\r\n",ct);
        return new(entries.Count>0,"Domain and IP lists refreshed",new {ipRanges=entries.Count,failedFeeds=failures});
    }
    private static bool ValidCidr(string input)
    {
        var parts=input.Split('/');
        return parts.Length==2 && IPAddress.TryParse(parts[0],out var ip)&&
            int.TryParse(parts[1],out var mask)&&mask>=0&&mask<= (ip.AddressFamily==AddressFamily.InterNetwork?32:128);
    }
    private async Task<RepairResult> SaveTestDomainsAsync(string[]? domains,CancellationToken ct)
    {
        if(domains is null || domains.Length is < 1 or > 25 || domains.Any(x=>!DomainAllowed(x)))return new(false,"Supply 1–25 valid DNS hostnames");
        await SaveAtomicAsync(TestDomainsFile,string.Join("\n",domains.Distinct(StringComparer.OrdinalIgnoreCase))+"\n",ct);
        return new(true,"Strategy test domains saved",new {domains});
    }
    private async Task<RepairResult> StrategyMapAsync(CancellationToken ct)
    {
        await Task.CompletedTask;
        return new(true,"Last strategy benchmark",new {best=File.Exists(Path.Combine(_options.Root,"best_strategy.txt"))?File.ReadAllText(Path.Combine(_options.Root,"best_strategy.txt")).Trim():null,
            domains=LoadDomains(TestDomainsFile),map=File.Exists(StrategyMapFile)?ReadShort(StrategyMapFile):null,scores=_scores});
    }
    private async Task<RepairResult> SetDnsAsync(string primary,string secondary,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(!IPAddress.TryParse(primary,out var p)||p.AddressFamily!=AddressFamily.InterNetwork||
           !IPAddress.TryParse(secondary,out var s)||s.AddressFamily!=AddressFamily.InterNetwork)return new(false,"Invalid IPv4 DNS servers");
        var adapter=NetworkInterface.GetAllNetworkInterfaces().FirstOrDefault(i=>i.OperationalStatus==OperationalStatus.Up&&
            i.NetworkInterfaceType!=NetworkInterfaceType.Loopback&&i.NetworkInterfaceType!=NetworkInterfaceType.Tunnel&&
            i.GetIPProperties().GatewayAddresses.Count>0);
        if(adapter is null)return new(false,"No eligible active network adapter");
        var props=adapter.GetIPProperties();
        var existing=props.DnsAddresses.Where(x=>x.AddressFamily==AddressFamily.InterNetwork).Select(x=>x.ToString()).ToArray();
        if(!File.Exists(DnsSnapshot))await SaveAtomicAsync(DnsSnapshot,JsonSerializer.Serialize(new DnsState(adapter.Name,existing)),ct);
        var change=await SetDnsServersAsync(adapter.Name,[primary,secondary],ct);
        if(!change.Success)
        {
            if(existing.Length>0)await SetDnsServersAsync(adapter.Name,existing,CancellationToken.None);
            return change;
        }
        await RunArgsAsync("ipconfig.exe",["/flushdns"],ct,30);
        return new(true,"DNS changed; original settings saved for rollback",new {adapter=adapter.Name,primary,secondary});
    }
    private sealed record DnsState(string Adapter,string[] Servers);
    private static async Task<RepairResult> SetDnsServersAsync(string adapter,string[] servers,CancellationToken ct)
    {
        if(servers.Length==0)
        {
            var dhcp=await RunArgsAsync("netsh.exe",["interface","ipv4","set","dnsservers","name="+adapter,"source=dhcp"],ct);
            return new(dhcp.Code==0,"Restore DHCP DNS",dhcp.Output);
        }
        var first=await RunArgsAsync("netsh.exe",["interface","ipv4","set","dnsservers","name="+adapter,"source=static","address="+servers[0],"register=primary","validate=no"],ct);
        if(first.Code!=0)return new(false,"Unable to set primary DNS",first.Output);
        for(var i=1;i<servers.Length;i++)
        {
            var extra=await RunArgsAsync("netsh.exe",["interface","ipv4","add","dnsservers","name="+adapter,"address="+servers[i],"index="+(i+1),"validate=no"],ct);
            if(extra.Code!=0)return new(false,"Unable to set secondary DNS",extra.Output);
        }
        return new(true,"DNS servers set");
    }
    private async Task<RepairResult> RestoreDnsAsync(CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(!File.Exists(DnsSnapshot))return new(false,"No saved DNS baseline");
        var backup=JsonSerializer.Deserialize<DnsState>(await File.ReadAllTextAsync(DnsSnapshot,ct));
        if(backup is null)return new(false,"Invalid DNS baseline");
        // If the adapter originally had DHCP, restoring exact DHCP origin requires reading the registry;
        // keep explicit servers as best-effort and expose this limitation.
        var restored=await SetDnsServersAsync(backup.Adapter,backup.Servers,ct);
        if(restored.Success)File.Delete(DnsSnapshot);
        return restored;
    }
    private async Task<RepairResult> RobloxHostsAsync(bool restore,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        var hosts=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),@"drivers\etc\hosts");
        if(!File.Exists(hosts))return new(false,"Windows hosts file not found");
        if(restore)
        {
            if(!File.Exists(HostsBackup))return new(false,"No hosts backup exists");
            File.Copy(HostsBackup,hosts,true);File.Delete(HostsBackup);
            await RunArgsAsync("ipconfig.exe",["/flushdns"],ct);
            return new(true,"Hosts file restored from snapshot");
        }
        if(File.Exists(HostsBackup))return new(false,"Hosts changes already active; restore before reapplying");
        var resolved=(await Dns.GetHostAddressesAsync("tr.rbxcdn.com",ct)).Where(x=>x.AddressFamily==AddressFamily.InterNetwork).ToArray();
        if(resolved.Length==0)return new(false,"DNS did not return a valid Roblox CDN address");
        var original=await File.ReadAllTextAsync(hosts,ct);
        File.Copy(hosts,HostsBackup,false);
        try
        {
            var lines=original.Split('\n').Where(x=>!x.Trim().EndsWith("tr.rbxcdn.com",StringComparison.OrdinalIgnoreCase)).ToArray();
            var changed=string.Join('\n',lines).TrimEnd()+"\r\n"+resolved[0]+" tr.rbxcdn.com # MysticEgo managed\r\n";
            await File.WriteAllTextAsync(hosts,changed,ct);
            await RunArgsAsync("ipconfig.exe",["/flushdns"],ct);
            return new(true,"Roblox CDN hosts mapping updated; rollback available",new {ip=resolved[0].ToString()});
        }
        catch{File.Copy(HostsBackup,hosts,true);throw;}
    }
    private static string? PubgMoviesPath()
    {
        var candidates=new List<string>();
        foreach(var drive in DriveInfo.GetDrives().Where(d=>d.IsReady&&d.DriveType==DriveType.Fixed))
        {
            var root=drive.RootDirectory.FullName;
            candidates.AddRange(new []{
                @"SteamLibrary\steamapps\common\PUBG\TslGame\Content\Movies",
                @"Steam\steamapps\common\PUBG\TslGame\Content\Movies",
                @"Games\Steam\steamapps\common\PUBG\TslGame\Content\Movies",
                @"PUBG\TslGame\Content\Movies",
                @"TslGame\Content\Movies"}.Select(s=>Path.Combine(root,s)));
        }
        return candidates.FirstOrDefault(Directory.Exists);
    }
    private sealed record MovedFile(string Original,string Quarantined);
    private async Task<RepairResult> PubgVideosAsync(bool restore,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(restore)
        {
            if(!File.Exists(QuarantineIndex))return new(false,"No PUBG video quarantine found");
            var files=JsonSerializer.Deserialize<MovedFile[]>(await File.ReadAllTextAsync(QuarantineIndex,ct))??[];
            foreach(var f in files)
            {
                if(!File.Exists(f.Quarantined)||File.Exists(f.Original))continue;
                Directory.CreateDirectory(Path.GetDirectoryName(f.Original)!);File.Move(f.Quarantined,f.Original);
            }
            File.Delete(QuarantineIndex);return new(true,"PUBG video files restored",new{count=files.Length});
        }
        if(File.Exists(QuarantineIndex))return new(false,"Existing quarantine must be restored first");
        var movies=PubgMoviesPath();
        if(movies is null)return new(false,"PUBG Movies directory not found in known Steam locations");
        var originals=Directory.GetFiles(movies,"*.mp4",SearchOption.TopDirectoryOnly);
        if(originals.Length==0)return new(true,"No PUBG trailers to quarantine");
        Directory.CreateDirectory(Quarantine);
        var moved=new List<MovedFile>();
        try
        {
            foreach(var file in originals)
            {
                var destination=Path.Combine(Quarantine,Guid.NewGuid().ToString("N")+"-"+Path.GetFileName(file));
                File.Move(file,destination);moved.Add(new(file,destination));
            }
            await SaveAtomicAsync(QuarantineIndex,JsonSerializer.Serialize(moved),ct);
        }
        catch
        {
            foreach(var f in moved.Where(x=>File.Exists(x.Quarantined)))File.Move(f.Quarantined,f.Original,true);
            throw;
        }
        return new(true,"PUBG trailers moved to reversible quarantine, not deleted",new {count=moved.Count,path=movies});
    }
    private async Task<RepairResult> TelegramAsync(string mode,CancellationToken ct)
    {
        if(mode=="proxy")
        {
            if(!File.Exists(WarpCli))return new(false,"Install WARP before setting Telegram proxy");
            var result=await WarpAsync("proxy",ct);
            if(!result.Success)return result;
            return new(true,"Open Telegram's proxy deeplink in the interactive client",new {deepLink=TelegramProxyUri,host="127.0.0.1",port=40000});
        }
        if(mode=="global")return await WarpAsync("warp",ct);
        if(mode=="off")return await WarpAsync("off",ct);
        return new(false,"Unsupported Telegram mode");
    }
    private async Task<RepairResult> GameFixAsync(string game,CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        var list=game=="pubg-fortnite"?PubgDomains:PhasmophobiaDomains;
        var update=await AddDomainsAsync(list,ct);
        if(!update.Success)return update;
        var zapret=await StartStrategyAsync(_activeStrategy??"general.bat",ct);
        if(!zapret.Success)return new(false,"Zapret failed to start",zapret);
        var warp=await WarpAsync("warp",ct);
        return new(warp.Success,warp.Success?"Game connectivity profile activated":"WARP connection failed; Zapret may still be running",new {game,warp,zapret});
    }
    private async Task<RepairResult> UpdateTelegramIpRangesAsync(CancellationToken ct)
    {
        if(!_options.AllowSystemChanges)return new(false,"System changes disabled by administrator");
        if(!File.Exists(WarpCli))return new(false,"Cloudflare WARP is required");
        // Requires a user-established WARP proxy on localhost:40000. Does not change WARP mode silently.
        using var handler=new HttpClientHandler { Proxy=new WebProxy("socks5://127.0.0.1:40000"),UseProxy=true };
        using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(35)};
        var raw=await client.GetStringAsync("https://core.telegram.org/resources/cidr.txt",ct);
        var ranges=raw.Split('\n',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
            .Where(ValidCidr).Take(2000).ToArray();
        if(ranges.Length==0)return new(false,"Telegram returned no valid CIDR ranges");
        Directory.CreateDirectory(Lists);
        var file=Path.Combine(Lists,"ipset-user.txt");
        var current=File.Exists(file)?File.ReadAllLines(file).Where(ValidCidr):[];
        await SaveAtomicAsync(file,string.Join("\r\n",current.Concat(ranges).Distinct(StringComparer.OrdinalIgnoreCase))+"\r\n",ct);
        return new(true,"Telegram CIDR ranges imported through existing WARP SOCKS5 proxy",new{count=ranges.Length});
    }
    private async Task<RepairResult> StartupMaintenanceAsync(CancellationToken ct)
    {
        if(!File.Exists(Path.Combine(Installed,"general.bat")))
        {
            var fetched=await UpdateZapretAsync(ct);
            if(!fetched.Success)return fetched;
        }
        var strategy=File.Exists(Path.Combine(_options.Root,"best_strategy.txt"))?
            (await File.ReadAllTextAsync(Path.Combine(_options.Root,"best_strategy.txt"),ct)).Trim():"";
        if(strategy.Length>0 && Strategies().Contains(strategy,StringComparer.OrdinalIgnoreCase))
            return new(true,"Zapret available with previously tested strategy",new{strategy});
        return await TestStrategiesAsync(ct);
    }
    private async Task<RepairResult> ExecuteExtendedAsync(RepairOperation request,CancellationToken ct)
    {
        switch(request.Action)
        {
            case "startup-maintenance": return await StartupMaintenanceAsync(ct);
            case "telegram-ips-update": return await UpdateTelegramIpRangesAsync(ct);
            case "strategy-map": return await StrategyMapAsync(ct);
            case "test-domains-set": return await SaveTestDomainsAsync(request.Domains,ct);
            case "test-domains-get": return new(true,"Strategy test hosts",LoadDomains(TestDomainsFile));
            case "update-lists": return await RefreshListsAsync(ct);
            case "game-pubg-fortnite": return await GameFixAsync("pubg-fortnite",ct);
            case "game-phasmophobia": return await GameFixAsync("phasmophobia",ct);
            case "game-pubg-trailers": return await PubgVideosAsync(false,ct);
            case "game-pubg-trailers-restore": return await PubgVideosAsync(true,ct);
            case "game-roblox-hosts": return await RobloxHostsAsync(false,ct);
            case "game-roblox-hosts-restore": return await RobloxHostsAsync(true,ct);
            case "game-roblox-launch": return await LaunchAppAsync("roblox",ct);
            case "game-roblox-install": return await PackagesAsync("install","roblox",ct);
            case "game-roblox-reinstall":
                if(!_options.AllowPackageChanges)return new(false,"Package changes disabled");
                var uninstall=await PackagesAsync("uninstall","roblox",ct);
                if(!uninstall.Success)return uninstall;
                return await PackagesAsync("install","roblox",ct);
            case "game-warzone-dns": return await SetDnsAsync("1.1.1.1","1.0.0.1",ct);
            case "game-pubg-dns": return await SetDnsAsync("77.88.8.8","77.88.8.1",ct);
            case "dns-restore": return await RestoreDnsAsync(ct);
            case "telegram-proxy": return await TelegramAsync("proxy",ct);
            case "telegram-global": return await TelegramAsync("global",ct);
            case "telegram-off": return await TelegramAsync("off",ct);
            case "signalrgb-watchdog-on": return await SetWatchdogAsync(true,ct);
            case "signalrgb-watchdog-off": return await SetWatchdogAsync(false,ct);
            case "launchers-all": return await PackageBatchAsync(ct);
        }
        if(request.Action.StartsWith("package-",StringComparison.Ordinal))
        {
            var parts=request.Action.Split('-',3);
            if(parts.Length!=3)return new(false,"Invalid application request");
            return parts[1] switch
            {
                "install" or "update" or "uninstall"=>await PackagesAsync(parts[1],parts[2],ct),
                "launch"=>await LaunchAppAsync(parts[2],ct),
                "autostart" when request.Mode is "on" or "off"=>await SetAutostartAsync(parts[2],request.Mode=="on",ct),
                _=>new(false,"Unknown application action")
            };
        }
        return new(false,"Operation not allowlisted");
    }
}

// Runs as part of the already installed Agent/Audit host, NOT a separate installer.
// The operator must explicitly enable the marker and admin policy.
public sealed class SignalRgbWatchdogService : BackgroundService
{
    private readonly NetworkRepairOptions _options;
    private readonly ILogger<SignalRgbWatchdogService> _logger;
    public SignalRgbWatchdogService(NetworkRepairOptions options,ILogger<SignalRgbWatchdogService> logger)
    { _options=options;_logger=logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var overLimitSamples=0;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);
                if(!OperatingSystem.IsWindows()||!_options.Enabled||!_options.AllowSystemChanges||
                   !File.Exists(Path.Combine(_options.Root,"signalrgb-watchdog.enabled")))continue;
                var processes=Process.GetProcessesByName("SignalRgb");
                try
                {
                    var used=processes.Sum(p=>p.WorkingSet64);
                    if(used>Math.Clamp(_options.SignalRgbMemoryLimitMb,512,16384)*1024L*1024L)overLimitSamples++;
                    else overLimitSamples=0;
                    if(overLimitSamples<3)continue;
                    _logger.LogWarning("SignalRGB exceeded memory limit for three samples; restarting its process");
                    foreach(var process in processes)
                        try{if(!process.HasExited)process.Kill();}catch(Exception e){_logger.LogWarning(e,"Cannot stop excessive SignalRGB process");}
                    overLimitSamples=0;
                }
                finally{foreach(var process in processes)process.Dispose();}
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){break;}
            catch(Exception e){_logger.LogWarning(e,"Optional SignalRGB watchdog failed");}
        }
    }
}
