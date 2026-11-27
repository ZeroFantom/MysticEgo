using MysticEgo.Capacity;
using MysticEgo.Distributed;
using MysticEgo.WorldAI;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra;

public sealed record CapacityPoint(DateTimeOffset At,double? CpuPercent,double? MemoryPercent,double? RxBytesPerSecond,double? TxBytesPerSecond,int? ActiveSlots,int? AvailableSlots,int? PendingJobs);
public sealed record CapacityView(CapacityReport Assessment,CapacitySample[] Samples,CapacityPoint[] History);

/// <summary>Shared cached analysis for all consumers. Collection is independent of HTTP request volume.</summary>
public sealed class CapacityMonitor(ServerTelemetry telemetry,ManagedNetwork network,WorldAiCluster models,WorldAiJobQueue jobs,
    NodeOptions node,IConfiguration config,ILogger<CapacityMonitor> log):BackgroundService
{
    private readonly CapacityRegistry registry=new();
    private readonly Queue<CapacityPoint> history=new();
    private CapacityView? latest;
    public CapacityView? Read()=>Volatile.Read(ref latest);
    public bool Accept(Guid identity,CapacitySample sample)
    {
        if(network.Find(identity) is not {Enabled:true} enrolled)return false;
        // Never trust sender-reported prices, node identity, network provision or role.
        return registry.Accept(sample with {NodeId=identity.ToString("D"),Role=enrolled.Kind,MonthlyCost=null,LinkMbps=null},DateTimeOffset.UtcNow);
    }
    private decimal? Cost(string key)=>decimal.TryParse(config[key],System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var cost)&&cost>=0?cost:null;
    private CapacitySample Price(CapacitySample row)=>row with{MonthlyCost=Cost("Capacity:Costs:"+row.NodeId)};
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(5));
        while(await timer.WaitForNextTickAsync(ct))
        {
            try
            {
                var now=DateTimeOffset.UtcNow;
                var readings=registry.Read().ToDictionary(s=>s.NodeId,StringComparer.Ordinal);
                var rows=network.Nodes().Where(n=>n.Enabled).Select(n=>readings.GetValueOrDefault(n.Id.ToString("D"))??
                    new CapacitySample(n.Id.ToString("D"),n.Kind,DateTimeOffset.MinValue,null,null,null,null,null,[],null,null)).ToList();
                var root="orchestra:"+node.NodeId;
                rows.RemoveAll(r=>r.NodeId==root);
                if(telemetry.History().LastOrDefault() is { } sample)
                {
                    var pools=await models.CapacityPools(ct);
                    var pending=await jobs.PendingCount(ct);
                    var link=config.GetValue<double?>("Capacity:PrimaryLinkMbps");
                    rows.Add(new(root,"orchestra",sample.At,sample.HostCpuPercent,
                        sample.HostMemoryTotalBytes is >0&&sample.HostMemoryUsedBytes is long used?used*100.0/sample.HostMemoryTotalBytes.Value:null,
                        sample.RxBytesPerSecond,sample.TxBytesPerSecond,link,pools,pending,null));
                }
                var priced=rows.Select(Price).ToArray();
                var budget=new CapacityBudget(config["Capacity:Currency"]??"RUB",Cost("Capacity:AnalyzerMonthlyCost"),
                    Cost("Capacity:ReserveMonthlyCost"),Cost("Capacity:MonthlyLimit"),config.GetValue("Capacity:MaxAdditionalPools",3));
                var report=CapacityAnalyzer.Analyze(priced,budget,now);
                var primary=priced.FirstOrDefault(r=>r.NodeId==root);
                var view=report.Nodes.FirstOrDefault(r=>r.NodeId==root);
                history.Enqueue(new(now,view?.CpuPercent,view?.MemoryPercent,primary?.RxBytesPerSecond,primary?.TxBytesPerSecond,
                    view?.ActiveSlots,view?.AvailableSlots,view?.PendingJobs));
                while(history.Count>180)history.Dequeue();
                Volatile.Write(ref latest,new CapacityView(report,priced,history.ToArray()));
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
            catch(Exception ex){log.LogWarning("Capacity evaluation failed: {Type}",ex.GetType().Name);}
        }
    }
}

/// <summary>Owner-only capacity dashboard; no provisioning or permission elevation is exposed here.</summary>
[ApiController,Authorize,Route("api/v1/orchestra/capacity")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CapacityController(CapacityMonitor monitor,DevelopmentTeam team):ControllerBase
{
    [HttpGet]
    public IActionResult Get()=>!team.IsOwner(User)?Forbid():monitor.Read() is { } view?Ok(view):StatusCode(503);
}

[ApiController,Authorize(Policy="node-peer"),Route("api/v1/stations/capacity")]
public sealed class CapacityIntakeController(CapacityMonitor monitor):ControllerBase
{
    [HttpPost,RequestSizeLimit(32768)]
    public IActionResult Post(CapacitySample sample)
    {
        if(!Guid.TryParse(User.FindFirst("identity_id")?.Value,out var id))return Forbid();
        return monitor.Accept(id,sample)?Accepted():BadRequest(new{code="capacity_sample_rejected"});
    }
}
