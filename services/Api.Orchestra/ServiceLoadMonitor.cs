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
    public sealed record Sample(string Unit,DateTimeOffset At,double? CpuPercent,long? MemoryBytes,long? Tasks,string? LoadState,string? ActiveState,string? SubState,string? Type=null,string? Result=null,long? LastCpuMilliseconds=null,long? LastPeakBytes=null,long? LastDurationMilliseconds=null,DateTimeOffset? LastFinishedAt=null,string? Measurement=null);
    private Sample[] latest=[];
    private readonly Dictionary<string,Sample> lastRuns=new(StringComparer.Ordinal);
    public static bool KnownUnit(string unit)=>Units.Contains(unit,StringComparer.Ordinal);
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
                var memory=ReadLong(Path.Combine(directory,"memory.current"));
                var tasks=ReadLong(Path.Combine(directory,"pids.current"));
                string? Get(string key)=>status?.GetValueOrDefault(key);
                var load=Get("LoadState");var active=Get("ActiveState");var sub=Get("SubState");
                var type=Get("Type");var result=Get("Result");
                var inactive=load=="loaded"&&active=="inactive"&&sub=="dead";
                var nonProcess=unit.EndsWith(".timer",StringComparison.Ordinal)||unit.EndsWith(".socket",StringComparison.Ordinal)||unit.EndsWith(".path",StringComparison.Ordinal);
                var measurement=nonProcess?"not-applicable":inactive?"idle":percentage.HasValue||memory.HasValue||tasks.HasValue?"live":"unavailable";
                if(inactive&&!nonProcess){if(tasks is null or 0)percentage=0;memory??=0;tasks??=0;}
                if(nonProcess){percentage=null;memory=null;tasks=null;}
                var start=Unsigned(Get("ExecMainStartTimestampMonotonic"));var end=Unsigned(Get("ExecMainExitTimestampMonotonic"));
                var finished=inactive||active=="failed";
                if(finished&&type=="oneshot"&&start>0&&end>=start)
                {
                    var duration=(end-start)/1000;
                    var stamp=ParseSystemdTime(Get("ExecMainExitTimestamp"));
                    var total=Unsigned(Get("CPUUsageNSec"))/1_000_000;
                    var peak=Unsigned(Get("MemoryPeak"));
                    var prior=lastRuns.GetValueOrDefault(unit);
                    if(stamp!=null&&(prior?.LastFinishedAt!=stamp||total.HasValue||peak.HasValue))
                        lastRuns[unit]=new(unit,at,null,null,null,load,active,sub,type,result,total??(prior?.LastFinishedAt==stamp?prior?.LastCpuMilliseconds:null),peak??(prior?.LastFinishedAt==stamp?prior?.LastPeakBytes:null),duration,stamp);
                }
                var last=lastRuns.GetValueOrDefault(unit);
                rows.Add(new(unit,at,percentage,memory,tasks,load,active,sub,type,result,last?.LastCpuMilliseconds,last?.LastPeakBytes,last?.LastDurationMilliseconds,last?.LastFinishedAt,measurement));
            }
            Volatile.Write(ref latest,rows.ToArray());
        }while(await timer.WaitForNextTickAsync(ct));
    }
    public static DateTimeOffset? ParseSystemdTime(string? raw)
    {
        if(raw is null)return null;
        return DateTimeOffset.TryParseExact(raw,"ddd yyyy-MM-dd HH:mm:ss \'UTC\'",CultureInfo.InvariantCulture,DateTimeStyles.AssumeUniversal|DateTimeStyles.AdjustToUniversal,out var time)?time:null;
    }
    private static long? Unsigned(string? value)=>long.TryParse(value,NumberStyles.None,CultureInfo.InvariantCulture,out var number)&&number>=0?number:null;
    private static async Task<Dictionary<string,Dictionary<string,string>>> UnitStates(CancellationToken ct)
    {
        var result=new Dictionary<string,Dictionary<string,string>>(StringComparer.Ordinal);
        try
        {
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(3));
            var start=new ProcessStartInfo("/usr/bin/systemctl"){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true};
            foreach(var arg in new[]{"show","--no-pager","--property=Id,LoadState,ActiveState,SubState,Type,Result,CPUUsageNSec,MemoryPeak,ExecMainStartTimestampMonotonic,ExecMainExitTimestampMonotonic,ExecMainExitTimestamp"}.Concat(Units))start.ArgumentList.Add(arg);
            start.Environment["LC_ALL"]="C";
            start.Environment["TZ"]="UTC";
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
                        result[id]=new(values,StringComparer.Ordinal);
                    values.Clear();
                }
                foreach(var line in raw.Replace("\r","").Split('\n'))
                {
                    if(line.Length==0){Flush();continue;}
                    var pair=line.Split('=',2);
                    if(pair.Length==2&&pair[1].Length<=120&&pair[1].All(c=>char.IsAsciiLetterOrDigit(c)||c is '-' or '_' or '@' or '.' or ' ' or ':' or '+'))values[pair[0]]=pair[1];
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
