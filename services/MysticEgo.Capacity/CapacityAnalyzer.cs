namespace MysticEgo.Capacity;

/// <summary>One physical inference pool, not a logical role alias. Unknown rates are represented by null.</summary>
public sealed record ModelPool(string Id,string Model,string Location,int Slots,int Active,bool Available,double? RequestsPerSecond=null);
/// <summary>Timestamped measurement from a single node. Network capacity and costs are configured or measured, never assumed.</summary>
public sealed record CapacitySample(string NodeId,string Role,DateTimeOffset At,double? CpuPercent,double? MemoryPercent,
    double? RxBytesPerSecond,double? TxBytesPerSecond,double? LinkMbps,ModelPool[] Pools,int? PendingJobs,decimal? MonthlyCost);
/// <summary>Prices are supplied in one configured currency per report. Null means unavailable, not free.</summary>
public sealed record CapacityBudget(string Currency,decimal? AnalyzerMonthlyCost,decimal? ReserveMonthlyCost,decimal? MonthlyLimit,int MaxAdditionalPools=3);
public sealed record NodeAssessment(string NodeId,string Role,string State,double? CpuPercent,double? MemoryPercent,double? NetworkPercent,
    int? AvailableSlots,int? ActiveSlots,int? PendingJobs,string[] Reasons);
public sealed record CapacityReport(DateTimeOffset At,NodeAssessment[] Nodes,int FreshNodes,int StaleNodes,int? AdditionalPools,
    decimal? KnownMonthlyCost,decimal? ProjectedMonthlyCost,bool? WithinBudget,string Currency,string[] Warnings);

/// <summary>Deterministic, side-effect-free advisory assessment. A model may explain results but cannot authorize spending.</summary>
public static class CapacityAnalyzer
{
    public static CapacityReport Analyze(IEnumerable<CapacitySample> samples,CapacityBudget budget,DateTimeOffset now)
    {
        if(budget.MaxAdditionalPools is <0 or >100 || string.IsNullOrWhiteSpace(budget.Currency) ||
            budget.AnalyzerMonthlyCost<0 || budget.ReserveMonthlyCost<0 || budget.MonthlyLimit<0)
            throw new ArgumentException("Invalid capacity budget");
        var rows=samples.GroupBy(x=>x.NodeId,StringComparer.Ordinal).Select(g=>g.MaxBy(x=>x.At)!).ToArray();
        if(rows.Length>2000)throw new ArgumentException("Too many nodes");
        var assessments=new List<NodeAssessment>();
        var warnings=new List<string>();
        var fresh=0;var stale=0;var reserve=0;var observedPools=false;
        foreach(var row in rows)
        {
            if(row.At>now.AddSeconds(5)||now-row.At>TimeSpan.FromSeconds(30))
            {
                stale++;assessments.Add(new(row.NodeId,row.Role,"stale",null,null,null,null,null,null,["Telemetry unavailable or stale"]));continue;
            }
            fresh++;
            var cpu=Percent(row.CpuPercent);var memory=Percent(row.MemoryPercent);
            var network=Number(row.LinkMbps) is >0 and var speed && Number(row.RxBytesPerSecond) is double rx &&
                Number(row.TxBytesPerSecond) is double tx ? Math.Max(rx,tx)*8/(speed*1_000_000)*100 : (double?)null;
            var pools=(row.Pools??[]).Where(x=>x.Slots is >0 and <=1024&&x.Active>=0&&x.Active<=1024)
                .GroupBy(x=>x.Id,StringComparer.Ordinal).Select(g=>new{Slots=g.Max(p=>p.Slots),Active=g.Max(p=>p.Active),Available=g.All(p=>p.Available)}).ToArray();
            int? slots=pools.Length==0?null:pools.Where(p=>p.Available).Sum(p=>p.Slots);
            int? active=pools.Length==0?null:pools.Sum(p=>p.Active);
            var reasons=new List<string>();
            if(cpu>=85)reasons.Add("CPU pressure");
            if(memory>=85)reasons.Add("Memory pressure");
            if(network>=80)reasons.Add("Network pressure");
            if(slots is int capacity)
            {
                observedPools=true;
                if(capacity==0){reasons.Add("No available inference pool");if(row.PendingJobs>0)reserve++;}
                else if(active>=capacity&&row.PendingJobs>0){reasons.Add("Inference saturation with backlog");reserve++;}
            }
            if(row.PendingJobs>0&&slots is null)reasons.Add("Inference backlog; capacity unknown");
            var status=reasons.Count>0?"attention":cpu is null&&memory is null&&network is null&&slots is null?"unknown":"observed";
            assessments.Add(new(row.NodeId,row.Role,status,cpu,memory,network,slots,active,row.PendingJobs,reasons.ToArray()));
        }
        if(stale>0)warnings.Add("Stale nodes excluded from demand recommendations.");
        if(!observedPools)warnings.Add("No measured inference pool capacity.");
        warnings.Add("Pool estimate is a bounded backlog heuristic, not a hardware sizing or token throughput forecast.");
        int? additional=observedPools?Math.Min(reserve,budget.MaxAdditionalPools):null;
        // Do not silently replace missing prices, stale inventory or analyzer overhead with zero.
        decimal? known=rows.Length>0&&rows.All(x=>x.MonthlyCost is >=0)&&budget.AnalyzerMonthlyCost is >=0
            ?rows.Sum(x=>x.MonthlyCost!.Value)+budget.AnalyzerMonthlyCost.Value:null;
        decimal? projected=known is decimal total&&additional is int count&&(count==0||budget.ReserveMonthlyCost is >=0)
            ?total+count*(budget.ReserveMonthlyCost??0):null;
        bool? within=projected.HasValue&&budget.MonthlyLimit.HasValue?projected<=budget.MonthlyLimit:null;
        if(projected is null)warnings.Add("Cost projection incomplete; no automated provisioning permitted.");
        return new(now,assessments.ToArray(),fresh,stale,additional,known,projected,within,budget.Currency,warnings.ToArray());
    }
    private static double? Number(double? value)=>value is double x&&double.IsFinite(x)&&x>=0?x:null;
    private static double? Percent(double? value)=>Number(value) is double x&&x<=100?x:null;
}
