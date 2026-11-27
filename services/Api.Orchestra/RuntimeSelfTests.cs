using MysticEgo.Distributed;
using MysticEgo.Api.Orchestra.Controllers;
using System.Diagnostics;

namespace MysticEgo.Api.Orchestra;

/// <summary>Read-only runtime assertions over independently collected observations. No test writes production
/// data, starts a model, changes a device, or provisions resources. CI remains responsible for full coverage.</summary>
public sealed class RuntimeSelfTests(DatabaseMaintenance database,OptionalDependencyState peers,ServiceLoadMonitor services,ServerTelemetry host,ILogger<RuntimeSelfTests> log):BackgroundService
{
    public sealed record Check(string Name,string Category,string Result,string Reason,DateTimeOffset At,long? LatencyMs=null);
    public sealed record Run(DateTimeOffset At,Check[] Checks);
    private readonly System.Collections.Concurrent.ConcurrentQueue<Run> history=new();
    public Run[] History()=>history.ToArray();
    public Run? Latest()=>history.LastOrDefault();
    public static string Evaluate(string? state,DateTimeOffset checkedAt,DateTimeOffset now,TimeSpan maxAge)
    {
        if(checkedAt>now.AddSeconds(5)||now-checkedAt>maxAge)return "unknown";
        return state switch{"available"=>"pass","degraded" or "unavailable"=>"fail","disabled" or "external"=>"skip",_=>"unknown"};
    }
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        do
        {
            try
            {
                var now=DateTimeOffset.UtcNow;var checks=new List<Check>();
                void Add(string name,string category,string result,string reason,DateTimeOffset at,long? latency=null)
                    =>checks.Add(new(name,category,result,reason,at,latency));
                var db=database.Snapshot;
                Add("database.connectivity","storage",Evaluate(db.State,db.CheckedAt,now,TimeSpan.FromSeconds(45)),"Read-only database probe",db.CheckedAt);
                var snapshots=peers.Snapshot();
                foreach(var name in new[]{"auth","orchestra","global","local"})
                {
                    var p=snapshots.FirstOrDefault(p=>p.Name==name);
                    Add(name+".health","network",p is null?"unknown":Evaluate(p.State,p.CheckedAt,now,TimeSpan.FromSeconds(60)),
                        p is null?"No configured measurement":"HTTP health observation",p?.CheckedAt??now,p?.LatencyMs);
                }
                var report=await InfrastructureMaintenanceController.ReadAsync(database,ct);
                foreach(var name in new[]{"docker","lmstudio","voice","postgres-service"})
                {
                    var c=report.Components.FirstOrDefault(c=>c.Name==name);
                    Add(name+".readiness","infrastructure",c is null?"unknown":Evaluate(c.State,c.CheckedAt,now,TimeSpan.FromMinutes(3)),
                        c is null?"No maintenance report":"Host maintenance observation",c?.CheckedAt??now);
                }
                var units=services.Read();
                foreach(var unit in new[]{"mysticego-orchestra-updater.timer","mysticego-infrastructure-reconcile.timer","mysticego-local-setup.socket"})
                {
                    var u=units.FirstOrDefault(u=>u.Unit==unit);
                    Add(unit,"maintenance",u is null||now-u.At>TimeSpan.FromSeconds(20)||u.ActiveState is null?"unknown":u.ActiveState=="active"?"pass":"fail",
                        "Expected active unit",u?.At??now);
                }
                var sample=host.History().LastOrDefault();
                Add("telemetry.freshness","monitoring",sample is null||now-sample.At>TimeSpan.FromSeconds(20)?"unknown":"pass","Host resource sampler",sample?.At??now);
                // These cannot be safely exercised against the production data plane.
                foreach(var name in new[]{"registration.mutations","session.revocation","native.automation","model.generation","voice.transcription","github.mutations","timeweb.provisioning","failover","browser.e2e"})
                    Add(name,"integration","skip","Requires isolated CI or explicitly authorized staging test",now);
                history.Enqueue(new(now,checks.ToArray()));
                while(history.Count>240)history.TryDequeue(out _);
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception e){log.LogWarning("Runtime self-check cycle failed: {Type}",e.GetType().Name);}
        }while(await timer.WaitForNextTickAsync(ct));
    }
}
