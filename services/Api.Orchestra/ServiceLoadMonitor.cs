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
        "docker.service","containerd.service","postgresql@16-mysticego.service","mysticego-maintenance-rescue.service"];
    public sealed record Sample(string Unit,DateTimeOffset At,double? CpuPercent,long? MemoryBytes,long? Tasks);
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
            foreach(var unit in Units)
            {
                var directory=Path.Combine("/sys/fs/cgroup/system.slice",unit);
                var at=DateTimeOffset.UtcNow;var tick=Stopwatch.GetTimestamp();
                var cpu=ReadCpu(directory);
                double? percentage=null;
                if(cpu is long value&&previous.TryGetValue(unit,out var old)&&value>=old.Cpu)
                    percentage=Math.Clamp((value-old.Cpu)/1_000_000.0/Math.Max(.001,Stopwatch.GetElapsedTime(old.Tick,tick).TotalSeconds)*100/Environment.ProcessorCount,0,100);
                if(cpu is long used)previous[unit]=(used,tick);else previous.Remove(unit);
                rows.Add(new(unit,at,percentage,ReadLong(Path.Combine(directory,"memory.current")),ReadLong(Path.Combine(directory,"pids.current"))));
            }
            Volatile.Write(ref latest,rows.ToArray());
        }while(await timer.WaitForNextTickAsync(ct));
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
