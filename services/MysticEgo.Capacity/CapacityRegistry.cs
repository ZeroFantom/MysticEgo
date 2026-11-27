namespace MysticEgo.Capacity;

/// <summary>Bounded in-memory latest measurements. Reject out-of-order or future samples.
/// Callers must authenticate and assign the node identity before calling Accept.</summary>
public sealed class CapacityRegistry
{
    private readonly object gate=new();
    private readonly Dictionary<string,CapacitySample> rows=new(StringComparer.Ordinal);
    public bool Accept(CapacitySample value,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(value.NodeId)||value.NodeId.Length>128||value.Role.Length>32||
            value.At>now.AddSeconds(5)||value.At<now.AddMinutes(-2)||value.Pools is null||value.Pools.Length>64||
            value.PendingJobs is <0 or >100000||
            value.Pools.Any(p=>p is null||string.IsNullOrWhiteSpace(p.Id)||p.Id.Length>128||p.Model is null||p.Model.Length>200||
                p.Location is not ("local" or "remote")||p.Slots is <0 or >1024||p.Active is <0 or >1024)||
            !Valid(value.CpuPercent,100)||!Valid(value.MemoryPercent,100)||!Valid(value.RxBytesPerSecond,1e12)||
            !Valid(value.TxBytesPerSecond,1e12))return false;
        lock(gate)
        {
            if(rows.TryGetValue(value.NodeId,out var prior)&&prior.At>=value.At)return false;
            if(rows.Count>=2000&&!rows.ContainsKey(value.NodeId))return false;
            rows[value.NodeId]=value;return true;
        }
    }
    public CapacitySample[] Read(){lock(gate)return rows.Values.ToArray();}
    private static bool Valid(double? value,double max)=>value is null||double.IsFinite(value.Value)&&value.Value>=0&&value.Value<=max;
}
