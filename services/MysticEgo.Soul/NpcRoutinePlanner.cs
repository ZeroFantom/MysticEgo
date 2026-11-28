namespace MysticEgo.Soul;

public sealed record NpcRoutineStop(long FromTick,string AreaId,string Activity);
public sealed record NpcRoutine(string EntityId,string WorldId,long PeriodTicks,IReadOnlyList<NpcRoutineStop> Stops);
public sealed record NpcRoutineObservation(string EntityId,string Activity,string DestinationArea,bool AtDestination);

/// <summary>Deterministic, operator-authored NPC routines. Only movement over existing directed
/// passages is emitted; no player movement, grants, economy, dialogue or generated commands.
/// Activity is descriptive intent, never proof that an action/reward occurred.</summary>
public sealed class NpcRoutinePlanner
{
    private readonly Dictionary<(string World,string From),string[]> edges;
    private readonly NpcRoutine[] routines;
    private readonly HashSet<(string World,string Area)> areas;
    private static bool Valid(string? value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&
        value==value.Trim()&&!value.Any(char.IsControl);
    public NpcRoutinePlanner(IEnumerable<SimulationArea> topology,IEnumerable<SimulationPassage> passages,
        IEnumerable<NpcRoutine> schedules)
    {
        areas=topology.Select(a=>(a.WorldId,a.AreaId)).ToHashSet();
        edges=passages.GroupBy(p=>(p.WorldId,p.FromArea)).ToDictionary(g=>g.Key,
            g=>g.Select(p=>p.ToArea).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray());
        var seen=new HashSet<string>(StringComparer.Ordinal);
        var list=new List<NpcRoutine>();
        foreach(var routine in schedules)
        {
            if(routine is null||list.Count>=64||!Valid(routine.EntityId,256)||!Valid(routine.WorldId,256)||
                !seen.Add(routine.EntityId)||routine.PeriodTicks is <1 or >1000000||
                routine.Stops is null||routine.Stops.Count is <1 or >32)
                throw new ArgumentException("Invalid NPC routine.");
            var stops=routine.Stops.ToArray();
            if(stops.Any(s=>s is null||s.FromTick<0||s.FromTick>=routine.PeriodTicks||
                !areas.Contains((routine.WorldId,s.AreaId))||!Valid(s.Activity,80)))
                throw new ArgumentException("Invalid NPC routine stop.");
            Array.Sort(stops,(a,b)=>a.FromTick.CompareTo(b.FromTick));
            if(stops[0].FromTick!=0||stops.Select(s=>s.FromTick).Distinct().Count()!=stops.Length)
                throw new ArgumentException("NPC routine requires unique phases starting at zero.");
            list.Add(routine with {Stops=Array.AsReadOnly(stops)});
        }
        routines=list.OrderBy(x=>x.EntityId,StringComparer.Ordinal).ToArray();
    }

    private IEnumerable<(WorldPlacement Placement,NpcRoutineStop Stop)> Resolve(SpiritualWorldSnapshot snapshot,long tick)
    {
        if(tick<0)yield break;
        var placements=snapshot.Placements.ToDictionary(p=>p.Presence.EntityId,StringComparer.Ordinal);
        foreach(var routine in routines)
        {
            if(!placements.TryGetValue(routine.EntityId,out var placement)||placement.AccountId is not null||
                placement.Presence.Kind!=SpiritualParticipantKind.Npc||placement.Presence.WorldId!=routine.WorldId||
                !areas.Contains((routine.WorldId,placement.Presence.AreaId)))continue;
            var phase=tick%routine.PeriodTicks;
            yield return (placement,routine.Stops.Last(s=>s.FromTick<=phase));
        }
    }

    // Shortest directed path, stable ordinal tie-breaking; at most one passage per tick.
    private string? NextHop(string world,string from,string to)
    {
        if(from==to)return null;
        var queue=new Queue<(string Area,string First)>();
        var visited=new HashSet<string>(StringComparer.Ordinal){from};
        foreach(var neighbor in edges.GetValueOrDefault((world,from))??[])
            if(areas.Contains((world,neighbor))&&visited.Add(neighbor))queue.Enqueue((neighbor,neighbor));
        while(queue.TryDequeue(out var node))
        {
            if(node.Area==to)return node.First;
            foreach(var neighbor in edges.GetValueOrDefault((world,node.Area))??[])
                if(areas.Contains((world,neighbor))&&visited.Add(neighbor))queue.Enqueue((neighbor,node.First));
        }
        return null;
    }

    public IReadOnlyList<WorldSimulationEvent> Plan(SpiritualWorldSnapshot snapshot,long tick)
    {
        var result=new List<WorldSimulationEvent>();
        foreach(var (placement,stop) in Resolve(snapshot,tick))
        {
            var p=placement.Presence;
            if(NextHop(p.WorldId,p.AreaId,stop.AreaId) is { } hop)
                result.Add(new MoveWorldEntity(p.EntityId,hop));
        }
        return result;
    }

    public IReadOnlyList<NpcRoutineObservation> Observe(SpiritualWorldSnapshot snapshot,long tick)=>
        Resolve(snapshot,tick).Select(x=>new NpcRoutineObservation(x.Placement.Presence.EntityId,
            x.Stop.Activity,x.Stop.AreaId,x.Placement.Presence.AreaId==x.Stop.AreaId)).ToArray();
}
