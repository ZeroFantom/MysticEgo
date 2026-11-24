using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;

/// <summary>
/// Optional, disabled-by-default Orchestra-side runner for centrally approved native plans.
/// Runs exactly one preconfigured unified installer executable against previously validated plans.
/// Does not accept shell commands, executable paths, SSH hosts or arbitrary arguments from HTTP requests.
/// </summary>
public sealed class NativeDeploymentJobs(
    IConfiguration config,
    ILogger<NativeDeploymentJobs> logger) : BackgroundService
{
    private readonly Channel<string> _pending = Channel.CreateBounded<string>(new BoundedChannelOptions(10)
    {
        SingleReader=true,SingleWriter=false,FullMode=BoundedChannelFullMode.Wait
    });
    private readonly ConcurrentDictionary<string,Job> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _oneAtATime=new(1,1);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private string PlansFolder => Path.GetFullPath(config["Orchestra:NativePlansPath"]??Path.Combine(AppContext.BaseDirectory,"data","native-plans"));
    private string JobFolder => Path.GetFullPath(config["Orchestra:NativeJobsPath"]??Path.Combine(AppContext.BaseDirectory,"data","native-jobs"));
    internal bool Enabled => config.GetValue("Orchestra:NativeDeploymentRunner:Enabled",false);

    internal (string? Error,Job? Accepted) Enqueue(string planId,string requestedBy)
    {
        if(!Enabled)return ("Runner is disabled in Orchestra configuration",null);
        if(!System.Text.RegularExpressions.Regex.IsMatch(planId,@"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))return ("Invalid plan id",null);
        var file=Path.Combine(PlansFolder,planId+".json");
        if(!File.Exists(file))return ("Plan does not exist",null);
        var installer=config["Orchestra:NativeDeploymentRunner:InstallerPath"];
        var root=config["Orchestra:NativeDeploymentRunner:SourceRoot"];
        if(string.IsNullOrWhiteSpace(installer)||!File.Exists(installer)||string.IsNullOrWhiteSpace(root)||!File.Exists(Path.Combine(root,"MysticEgo.Distributed.slnx")))
            return ("InstallerPath/SourceRoot are not configured or invalid",null);
        var snapshot=File.ReadAllBytes(file);
        try
        {
            using var doc=JsonDocument.Parse(snapshot);
            var targets=doc.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
            if(targets.Length==0||targets.Length>100)return ("Invalid node count",null);
            var allowlist=config.GetSection("Orchestra:NativeDeploymentRunner:AllowedHosts").Get<string[]>()??[];
            foreach(var t in targets)
            {
                var host=t.GetProperty("host").GetString()??"";
                // No arbitrary remote targets. A literal explicit allowlist is required.
                if(!allowlist.Contains(host,StringComparer.OrdinalIgnoreCase))return ("Host is not in NativeDeploymentRunner AllowedHosts: "+host,null);
                if(t.GetProperty("roles").EnumerateArray().Any(r=>string.Equals(r.GetString(),"auth",StringComparison.OrdinalIgnoreCase)))
                    return ("Auth bootstrap from Orchestra runner is prohibited; initialize Auth securely beforehand",null);
                if(host=="local" && t.GetProperty("roles").EnumerateArray().Any(r=>string.Equals(r.GetString(),"orchestra",StringComparison.OrdinalIgnoreCase)))
                    return ("Orchestra cannot restart itself from its deployment worker",null);
            }
        }
        catch(Exception e) when(e is JsonException or InvalidOperationException or KeyNotFoundException)
        {return ("Invalid saved plan",null);}
        var job=new Job(Guid.NewGuid().ToString("N"),planId,requestedBy,DateTimeOffset.UtcNow,"queued",null,null,null);
        _jobs[job.Id]=job;
        // Immutable snapshot protects the execution decision against edits to a plan after approval.
        Directory.CreateDirectory(JobFolder);
        File.WriteAllBytes(Path.Combine(JobFolder,job.Id+".plan.json"),snapshot);
        WriteJob(job);
        if(!_pending.Writer.TryWrite(job.Id))
        {
            _jobs.TryRemove(job.Id,out _);
            File.Delete(Path.Combine(JobFolder,job.Id+".json"));
            File.Delete(Path.Combine(JobFolder,job.Id+".plan.json"));
            return ("Deployment queue is full",null);
        }
        return (null,job);
    }
    internal Job? Find(string id)=>_jobs.TryGetValue(id,out var job)?job:null;
    internal IReadOnlyList<Job> List()=>_jobs.Values.OrderByDescending(x=>x.CreatedAt).Take(100).ToArray();
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Explicitly mark previously interrupted jobs as such; never automatically replay unknown side effects.
        if(Enabled)
        {
            Directory.CreateDirectory(JobFolder);
            foreach(var file in Directory.EnumerateFiles(JobFolder,"*.json").Where(p=>!p.EndsWith(".plan.json",StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var previous=JsonSerializer.Deserialize<Job>(await File.ReadAllTextAsync(file,stoppingToken),Json);
                    if(previous==null)continue;
                    if(previous.Status is "running" or "queued")previous=previous with {Status="interrupted",Message="Orchestra restarted. Manual verification required."};
                    _jobs[previous.Id]=previous;
                    WriteJob(previous);
                }
                catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException)
                {logger.LogWarning("Failed loading deployment job metadata: {ErrorType}",e.GetType().Name);}
            }
        }
        await foreach(var id in _pending.Reader.ReadAllAsync(stoppingToken))
        {
            if(!_jobs.TryGetValue(id,out var job))continue;
            await _oneAtATime.WaitAsync(stoppingToken);
            try {await ExecuteOneAsync(job,stoppingToken);}
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested)
            {
                Update(job with {Status="interrupted",Message="Orchestra stopped during deployment. Check remote hosts before retry."});
            }
            catch(Exception e)
            {
                logger.LogError(e,"Native deployment job failed: {JobId}",job.Id);
                Update(job with {Status="failed",Message=e.GetType().Name,FinishedAt=DateTimeOffset.UtcNow});
            }
            finally {_oneAtATime.Release();}
        }
    }
    private async Task ExecuteOneAsync(Job job,CancellationToken ct)
    {
        var file=Path.Combine(JobFolder,job.Id+".plan.json");
        var installer=Path.GetFullPath(config["Orchestra:NativeDeploymentRunner:InstallerPath"]!);
        var root=Path.GetFullPath(config["Orchestra:NativeDeploymentRunner:SourceRoot"]!);
        var maxMinutes=Math.Clamp(config.GetValue("Orchestra:NativeDeploymentRunner:TimeoutMinutes",30),1,120);
        var args=new List<string>{"--mode","native","--manifest",file,"--root",root};
        if(config.GetValue("Orchestra:NativeDeploymentRunner:InstallServices",false))args.Add("--install-services");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(maxMinutes));
        var start=new ProcessStartInfo(installer){WorkingDirectory=root,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
        foreach(var arg in args)start.ArgumentList.Add(arg);
        // Installation uses only the Orchestra service account's pre-provisioned SSH identity.
        // Do not copy bearer tokens to child processes.
        start.Environment.Remove("MYSTICEGO_ORCHESTRA_TOKEN");
        Update(job with{Status="running",StartedAt=DateTimeOffset.UtcNow});
        using var process=Process.Start(start)??throw new InvalidOperationException("Installer did not start");
        var stdOut=process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stdErr=process.StandardError.ReadToEndAsync(timeout.Token);
        try {await process.WaitForExitAsync(timeout.Token);}
        catch(OperationCanceledException)
        {
            if(!process.HasExited)process.Kill(entireProcessTree:true);
            throw;
        }
        var output=(await stdOut)+"\n"+(await stdErr);
        if(output.Length>8000)output=output[^8000..];
        var status=process.ExitCode==0?"succeeded":"failed";
        Update(job with{Status=status,FinishedAt=DateTimeOffset.UtcNow,Message=output});
    }
    private void Update(Job job){_jobs[job.Id]=job;WriteJob(job);}
    private void WriteJob(Job job)
    {
        Directory.CreateDirectory(JobFolder);
        var path=Path.Combine(JobFolder,job.Id+".json");var tmp=path+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(job,Json));File.Move(tmp,path,true);
    }
    internal sealed record Job(string Id,string PlanId,string RequestedBy,DateTimeOffset CreatedAt,string Status,DateTimeOffset? StartedAt,DateTimeOffset? FinishedAt,string? Message);
}
