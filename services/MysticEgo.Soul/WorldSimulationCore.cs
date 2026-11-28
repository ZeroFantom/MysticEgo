namespace MysticEgo.Soul;

public sealed record SimulationArea(string WorldId,string AreaId,string Name);
public sealed record SimulationPassage(string WorldId,string FromArea,string ToArea);
public abstract record WorldSimulationEvent;
public sealed record SpawnWorldPlayer(string EntityId,Guid AccountId,string WorldId,string AreaId,string Name):WorldSimulationEvent;
public sealed record SpawnWorldNpc(string EntityId,string WorldId,string AreaId,string Name,SpiritualNpcProfile Profile):WorldSimulationEvent;
public sealed record MoveWorldEntity(string EntityId,string DestinationArea):WorldSimulationEvent;
public sealed record SelectWorldEmbodiment(Guid AccountId,string? EntityId):WorldSimulationEvent;
public sealed record RetireWorldEntity(string EntityId):WorldSimulationEvent;
public sealed record SetSpiritualPermission(string SenderId,string RecipientId,bool Allowed):WorldSimulationEvent;
public sealed record SetConfirmedWorldFriendship(string FirstId,string SecondId,bool Confirmed):WorldSimulationEvent;

/// <summary>Internal deterministic state transitions over a trusted, operator-provided topology.
/// No client, MCP, model-generated or messenger event is accepted directly. The caller must
/// authorize every event, including friendship confirmations and movement conditions.
/// Publication is atomic with lifetime fencing in WorldAuthorityStore.</summary>
public sealed class WorldSimulationCore
{
    private readonly WorldAuthorityStore store;
    private readonly Dictionary<(string World,string Area),string> areas=new();
    private readonly HashSet<(string World,string From,string To)> passages=new();
    private static bool Valid(string? text,int max)=>!string.IsNullOrWhiteSpace(text)&&text.Length<=max&&
        text==text.Trim()&&!text.Any(char.IsControl);
    public WorldSimulationCore(WorldAuthorityStore store,IEnumerable<SimulationArea> topology,
        IEnumerable<SimulationPassage> connections)
    {
        this.store=store;
        foreach(var area in topology)
        {
            if(area is null||!Valid(area.WorldId,256)||!Valid(area.AreaId,256)||!Valid(area.Name,80)||
                areas.Count>=2048||!areas.TryAdd((area.WorldId,area.AreaId),area.Name))
                throw new ArgumentException("Invalid simulation area.");
        }
        foreach(var route in connections)
        {
            if(route is null||route.FromArea==route.ToArea||!areas.ContainsKey((route.WorldId,route.FromArea))||
                !areas.ContainsKey((route.WorldId,route.ToArea))||passages.Count>=8192||
                !passages.Add((route.WorldId,route.FromArea,route.ToArea)))
                throw new ArgumentException("Invalid simulation passage.");
        }
    }

    public bool Apply(long expected,IReadOnlyList<WorldSimulationEvent> events,out long revision)
    {
        revision=expected;
        if(expected<0||events is null||events.Count is <1 or >64)return false;
        var current=store.Current();
        revision=current?.Revision??0;
        if(revision!=expected)return false;
        var previous=current?.Feed.Snapshot??new SpiritualWorldSnapshot([],[],[],[]);
        var placements=previous.Placements.ToDictionary(p=>p.Presence.EntityId,StringComparer.Ordinal);
        var active=previous.ActiveEmbodiments.ToDictionary(a=>a.AccountId,a=>a.EntityId);
        var names=previous.ContactLabels.ToDictionary(x=>x.EntityId,x=>x.Name,StringComparer.Ordinal);
        var profiles=previous.NpcProfiles.ToDictionary(x=>x.EntityId,StringComparer.Ordinal);
        var grants=previous.Permissions.Select(x=>(x.SenderId,x.RecipientId)).ToHashSet();
        var friends=previous.Friendships.Select(x=>Pair(x.FirstId,x.SecondId)).ToHashSet();
        // Retire then respawn in one batch is also prohibited.
        var removed=new HashSet<string>(StringComparer.Ordinal);
        if(placements.Values.Any(p=>!areas.ContainsKey((p.Presence.WorldId,p.Presence.AreaId))))return false;
        bool Spawn(string id,string world,string area,string name,SpiritualParticipantKind kind,Guid? owner)
        {
            if(!Valid(id,256)||!Valid(name,80)||!areas.ContainsKey((world,area))||
                placements.Count>=2048||removed.Contains(id)||
                !placements.TryAdd(id,new(new(id,world,area,kind),owner)))return false;
            names[id]=name;return true;
        }
        foreach(var e in events)
        {
            switch(e)
            {
                case SpawnWorldPlayer player:
                    if(player.AccountId==Guid.Empty||!Spawn(player.EntityId,player.WorldId,player.AreaId,player.Name,
                        SpiritualParticipantKind.Player,player.AccountId))return false;
                    break;
                case SpawnWorldNpc npc:
                    if(npc.Profile is null||npc.Profile.EntityId!=npc.EntityId||
                        !Spawn(npc.EntityId,npc.WorldId,npc.AreaId,npc.Name,SpiritualParticipantKind.Npc,null))return false;
                    profiles[npc.EntityId]=npc.Profile;
                    break;
                case MoveWorldEntity move:
                    if(!placements.TryGetValue(move.EntityId,out var location)||
                        !passages.Contains((location.Presence.WorldId,location.Presence.AreaId,move.DestinationArea)))return false;
                    placements[move.EntityId]=location with {Presence=location.Presence with {AreaId=move.DestinationArea}};
                    break;
                case SelectWorldEmbodiment select:
                    if(select.AccountId==Guid.Empty)return false;
                    if(select.EntityId is null){active.Remove(select.AccountId);break;}
                    if(!placements.TryGetValue(select.EntityId,out var selected)||selected.AccountId!=select.AccountId||
                        selected.Presence.Kind!=SpiritualParticipantKind.Player)return false;
                    active[select.AccountId]=select.EntityId;break;
                case RetireWorldEntity retire:
                    if(!placements.Remove(retire.EntityId))return false;
                    removed.Add(retire.EntityId);names.Remove(retire.EntityId);profiles.Remove(retire.EntityId);
                    foreach(var owner in active.Where(x=>x.Value==retire.EntityId).Select(x=>x.Key).ToArray())active.Remove(owner);
                    grants.RemoveWhere(x=>x.SenderId==retire.EntityId||x.RecipientId==retire.EntityId);
                    friends.RemoveWhere(x=>x.First==retire.EntityId||x.Second==retire.EntityId);
                    break;
                case SetSpiritualPermission grant:
                    if(grant.SenderId==grant.RecipientId||!placements.TryGetValue(grant.SenderId,out var sender)||
                        !placements.TryGetValue(grant.RecipientId,out var recipient)||
                        sender.Presence.WorldId!=recipient.Presence.WorldId||
                        (sender.AccountId is null&&recipient.AccountId is null))return false;
                    if(grant.Allowed)grants.Add((grant.SenderId,grant.RecipientId));
                    else grants.Remove((grant.SenderId,grant.RecipientId));
                    break;
                case SetConfirmedWorldFriendship friend:
                    if(friend.FirstId==friend.SecondId||!placements.TryGetValue(friend.FirstId,out var first)||
                        !placements.TryGetValue(friend.SecondId,out var second)||first.AccountId is null||
                        second.AccountId is null||first.AccountId==second.AccountId||
                        first.Presence.WorldId!=second.Presence.WorldId)return false;
                    var pair=Pair(friend.FirstId,friend.SecondId);
                    if(friend.Confirmed)friends.Add(pair);else friends.Remove(pair);
                    break;
                default:return false;
            }
        }
        // Intermediate identities created and removed in one batch would evade the lifetime
        // registry, so reject that case rather than leave a reusable ID.
        if(removed.Any(id=>!previous.Placements.Any(p=>p.Presence.EntityId==id)))return false;
        var next=new SpiritualWorldSnapshot(placements.Values.ToArray(),
            active.Select(x=>new ActiveEmbodiment(x.Key,x.Value)).ToArray(),
            grants.Select(x=>new SpiritualPermission(x.SenderId,x.RecipientId)).ToArray(),
            friends.Select(x=>new SpiritualFriendship(x.First,x.Second)).ToArray())
        {
            ContactLabels=names.Select(x=>new SpiritualContactLabel(x.Key,x.Value)).ToArray(),
            AreaLabels=placements.Values.Select(p=>(p.Presence.WorldId,p.Presence.AreaId)).Distinct()
                .Select(x=>new SpiritualAreaLabel(x.WorldId,x.AreaId,areas[x])).ToArray(),
            NpcProfiles=profiles.Values.ToArray()
        };
        try{return store.TryPublish(expected,next,out revision);}
        catch(ArgumentException){return false;}
    }

    private static (string First,string Second) Pair(string a,string b)=>
        string.CompareOrdinal(a,b)<0?(a,b):(b,a);
}
