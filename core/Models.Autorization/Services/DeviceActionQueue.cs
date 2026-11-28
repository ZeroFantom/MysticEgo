namespace Models.Autorization.Services;

// Short-lived account/session-bound requests, never an operating-system command queue.
public sealed class DeviceActionQueue
{
    public static readonly string[] Allowed=["open-assistant","open-collective","open-profile","open-avatars"];
    public sealed record AvatarSnapshot(string Motion,string Mood,double PhaseSeconds)
    {
        public bool Valid()=>Motion is ("idle" or "wave" or "nod" or "walk" or "sit" or "dance")&&Mood is ("neutral" or "happy" or "sad" or "angry" or "surprised")&&double.IsFinite(PhaseSeconds)&&PhaseSeconds>=0&&PhaseSeconds<=3600;
    }
    public sealed record Item(Guid Id,Guid Owner,Guid Source,Guid Target,string Action,DateTimeOffset Expires,string State,AvatarSnapshot? Avatar=null);
    private readonly object gate=new();
    private readonly Dictionary<Guid,Item> items=new();
    private readonly Dictionary<(Guid Owner,Guid Session),DateTimeOffset> receiving=new();
    private void Prune(DateTimeOffset now)
    {
        foreach(var id in items.Where(p=>p.Value.Expires<=now).Select(p=>p.Key).ToArray())items.Remove(id);
        foreach(var key in receiving.Where(p=>p.Value<=now).Select(p=>p.Key).ToArray())receiving.Remove(key);
    }
    public bool Presence(Guid owner,Guid session,bool enabled,DateTimeOffset now)
    {
        if(owner==Guid.Empty||session==Guid.Empty)return false;
        lock(gate)
        {
            Prune(now);var key=(owner,session);
            if(!enabled){receiving.Remove(key);return true;}
            if(!receiving.ContainsKey(key)&&receiving.Count>=1000)return false;
            receiving[key]=now.AddSeconds(35);return true;
        }
    }
    public HashSet<Guid> Ready(Guid owner,DateTimeOffset now)
    {lock(gate){Prune(now);return receiving.Keys.Where(k=>k.Owner==owner).Select(k=>k.Session).ToHashSet();}}
    public Item? Cancel(Guid owner,Guid session,Guid id,DateTimeOffset now)
    {
        lock(gate){Prune(now);if(!items.TryGetValue(id,out var item)||item.Owner!=owner||item.Source!=session||item.State!="pending")return null;
            var result=item with{State="cancelled"};items[id]=result;return result;}
    }
    public Item? Request(Guid owner,Guid source,Guid target,string action,DateTimeOffset now)
    {
        if(owner==Guid.Empty||source==Guid.Empty||target==Guid.Empty||source==target||!Allowed.Contains(action,StringComparer.Ordinal))return null;
        lock(gate)
        {
            Prune(now);
            if(items.Count>=500||items.Values.Count(i=>i.Owner==owner)>=20)return null;
            var item=new Item(Guid.NewGuid(),owner,source,target,action,now.AddMinutes(2),"pending");
            items.Add(item.Id,item);return item;
        }
    }
    public Item? Handoff(Guid owner,Guid source,Guid target,AvatarSnapshot snapshot,DateTimeOffset now)
    {
        if(owner==Guid.Empty||source==Guid.Empty||target==Guid.Empty||source==target||!snapshot.Valid())return null;
        lock(gate){
            Prune(now);
            if(!receiving.ContainsKey((owner,target))||items.Count>=500||items.Values.Count(i=>i.Owner==owner)>=20)return null;
            var item=new Item(Guid.NewGuid(),owner,source,target,"avatar-handoff",now.AddMinutes(2),"pending",snapshot);
            items.Add(item.Id,item);return item;
        }
    }
    public Item[] List(Guid owner,Guid session,DateTimeOffset now)
    {lock(gate){Prune(now);return items.Values.Where(x=>x.Owner==owner&&(x.Source==session||x.Target==session)).ToArray();}}
    public Item? Decide(Guid owner,Guid session,Guid id,bool approve,DateTimeOffset now)
    {
        lock(gate)
        {
            Prune(now);
            if(!items.TryGetValue(id,out var item)||item.Owner!=owner||item.Target!=session||item.State!="pending")return null;
            if(approve&&item.Avatar is not null&&!receiving.ContainsKey((owner,session)))return null;
            var result=item with{State=approve?"approved":"declined"};items[id]=result;return result;
        }
    }
}
