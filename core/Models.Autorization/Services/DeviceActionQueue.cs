namespace Models.Autorization.Services;

// Short-lived account/session-bound requests. No arbitrary OS command or URI.
public sealed class DeviceActionQueue
{
    public static readonly string[] Navigation=["open-assistant","open-collective","open-profile","open-avatars"];
    public static readonly string[] Allowed=[..Navigation,"signal-device","open-app-settings","show-device-info"];
    public sealed record Item(Guid Id,Guid Owner,Guid Source,Guid Target,string Action,DateTimeOffset Expires,string State);
    private sealed record Lease(DateTimeOffset Expires,string[] Actions);
    private readonly object gate=new();
    private readonly Dictionary<Guid,Item> items=new();
    private readonly Dictionary<(Guid Owner,Guid Session),Lease> receiving=new();
    private void CancelIncoming(Guid owner,Guid session)
    {
        foreach(var item in items.Values.Where(i=>i.Owner==owner&&i.Target==session&&i.State=="pending").ToArray())
            items[item.Id]=item with{State="cancelled"};
    }
    private void Prune(DateTimeOffset now)
    {
        foreach(var id in items.Where(p=>p.Value.Expires<=now).Select(p=>p.Key).ToArray())items.Remove(id);
        foreach(var key in receiving.Where(p=>p.Value.Expires<=now).Select(p=>p.Key).ToArray())
        {receiving.Remove(key);CancelIncoming(key.Owner,key.Session);}
    }
    public bool Presence(Guid owner,Guid session,bool enabled,DateTimeOffset now,string[]? actions=null)
    {
        if(owner==Guid.Empty||session==Guid.Empty)return false;
        var supported=actions??Navigation;
        if(enabled&&(supported.Length==0||supported.Length>Allowed.Length||supported.Distinct(StringComparer.Ordinal).Count()!=supported.Length||
            supported.Any(a=>!Allowed.Contains(a,StringComparer.Ordinal))))return false;
        lock(gate)
        {
            Prune(now);var key=(owner,session);
            if(!enabled){receiving.Remove(key);CancelIncoming(owner,session);return true;}
            if(!receiving.ContainsKey(key)&&receiving.Count>=1000)return false;
            receiving[key]=new(now.AddSeconds(35),supported.ToArray());
            foreach(var item in items.Values.Where(i=>i.Owner==owner&&i.Target==session&&i.State=="pending"&&!supported.Contains(i.Action,StringComparer.Ordinal)).ToArray())
                items[item.Id]=item with{State="cancelled"};
            return true;
        }
    }
    public Dictionary<Guid,string[]> Capabilities(Guid owner,DateTimeOffset now)
    {lock(gate){Prune(now);return receiving.Where(x=>x.Key.Owner==owner).ToDictionary(x=>x.Key.Session,x=>x.Value.Actions.ToArray());}}
    public HashSet<Guid> Ready(Guid owner,DateTimeOffset now)=>Capabilities(owner,now).Keys.ToHashSet();
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
            if(!receiving.TryGetValue((owner,target),out var lease)||!lease.Actions.Contains(action,StringComparer.Ordinal))return null;
            if(items.Count>=500||items.Values.Count(i=>i.Owner==owner)>=20)return null;
            var item=new Item(Guid.NewGuid(),owner,source,target,action,now.AddMinutes(2),"pending");
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
            if(approve&&(!receiving.TryGetValue((owner,session),out var lease)||!lease.Actions.Contains(item.Action,StringComparer.Ordinal)))return null;
            var result=item with{State=approve?"approved":"declined"};items[id]=result;return result;
        }
    }
}
