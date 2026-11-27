using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace MysticEgo.Api.Orchestra;

/// <summary>Read fixed systemd cgroup v2 counters without invoking privileged commands or accepting paths.
/// Orchestra and Auth share one service group; metrics are group totals, not separate process estimates.</summary>
public sealed class ServiceLoadMonitor : BackgroundService
{
    private static readonly string[] Units=["mysticego-orchestra.service","mysticego-orchestra-updater.service",
        "mysticego-infrastructure-reconcile.service","mysticego-lmstudio.service","mysticego-voice.service",
        "docker.service","containerd.service","postgresql@16-mysticego.service","postgresql.service","caddy.service",
        "mysticego-default-model.service","mysticego-maintenance-rescue.service","mysticego-maintenance-rescue.path",
        "mysticego-orchestra-updater.timer","mysticego-infrastructure-reconcile.timer","mysticego-local-setup.socket"];
    public sealed record Sample(string Unit,DateTimeOffset At,double? CpuPercent,long? MemoryBytes,long? Tasks,string? LoadState,string? ActiveState,string? SubState);
    private Sample[] latest=[];
    public Sample[] Read()=>Volatile.Read(ref latest);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if(!OperatingSystem.IsLinux())return;
        var previous=new Dictionary<string,(long Cpu,long Tick)>();
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        do
        {
            var rows=new List<Sample>();
            var states=await UnitStates(ct);
            foreach(var unit in Units)
            {
                var directory=Path.Combine("/sys/fs/cgroup/system.slice",unit);
                var at=DateTimeOffset.UtcNow;var tick=Stopwatch.GetTimestamp();
                var cpu=ReadCpu(directory);
                double? percentage=null;
                if(cpu is long value&&previous.TryGetValue(unit,out var old)&&value>=old.Cpu)
                    percentage=Math.Clamp((value-old.Cpu)/1_000_000.0/Math.Max(.001,Stopwatch.GetElapsedTime(old.Tick,tick).TotalSeconds)*100/Environment.ProcessorCount,0,100);
                if(cpu is long used)previous[unit]=(used,tick);else previous.Remove(unit);
                var status=states.GetValueOrDefault(unit);
                rows.Add(new(unit,at,percentage,ReadLong(Path.Combine(directory,"memory.current")),ReadLong(Path.Combine(directory,"pids.current")),status.Load,status.Active,status.Sub));
            }
            Volatile.Write(ref latest,rows.ToArray());
        }while(await timer.WaitForNextTickAsync(ct));
    }
    private static async Task<Dictionary<string,(string? Load,string? Active,string? Sub)>> UnitStates(CancellationToken ct)
    {
        var result=new Dictionary<string,(string?,string?,string?)>(StringComparer.Ordinal);
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var start=new ProcessStartInfo("/usr/bin/systemctl"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"show","--no-pager","--property=Id,LoadState,ActiveState,SubState"}.Concat(Units))start.ArgumentList.Add(arg);
            using var process=Process.Start(start);
            if(process is null)return result;
            try
            {
                var output=process.StandardOutput.ReadToEndAsync(deadline.Token);
                var error=process.StandardError.ReadToEndAsync(deadline.Token);
                await process.WaitForExitAsync(deadline.Token);
                await error;
                var raw=await output;
                if(raw.Length>32768)return result;
                var values=new Dictionary<string,string>(StringComparer.Ordinal);
                void Flush()
                {
                    if(values.TryGetValue("Id",out var id)&&Units.Contains(id))
                        result[id]=(values.GetValueOrDefault("LoadState"),values.GetValueOrDefault("ActiveState"),values.GetValueOrDefault("SubState"));
                    values.Clear();
                }
                foreach(var line in raw.Replace("\r","").Split('\n'))
                {
                    if(line.Length==0){Flush();continue;}
                    var pair=line.Split('=',2);
                    if(pair.Length==2&&pair[1].Length<=80&&pair[1].All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '@' or '.'))values[pair[0]]=pair[1];
                }
                Flush();
            }
            finally{if(!process.HasExited)process.Kill();}
        }
        catch(Exception e) when(e is IOException or System.ComponentModel.Win32Exception or InvalidOperationException||e is OperationCanceledException&&!ct.IsCancellationRequested){}
        return result;
    }
    private static long? ReadCpu(string directory)
    {
        try
        {
            var line=File.ReadLines(Path.Combine(directory,"cpu.stat")).FirstOrDefault(x=>x.StartsWith("usage_usec ",StringComparison.Ordinal));
            return line is not null&&long.TryParse(line[11..],out var value)&&value>=0?value:null;
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return null;}
    }
    private static long? ReadLong(string path)
    {
        try{return long.TryParse(File.ReadAllText(path).Trim(),NumberStyles.None,CultureInfo.InvariantCulture,out var value)?value:null;}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException){return null;}
    }
}
