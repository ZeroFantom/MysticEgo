namespace Models.Autorization.Services;

// Short-lived account/session-bound requests, never an operating-system command queue.
public sealed class DeviceActionQueue
{
    public static readonly string[] Allowed=["open-assistant","open-collective","open-profile","open-avatars"];
    public sealed record Item(Guid Id,Guid Owner,Guid Source,Guid Target,string Action,DateTimeOffset Expires,string State);
    private readonly object gate=new();
    private readonly Dictionary<Guid,Item> items=new();
    private void Prune(DateTimeOffset now)
    {
        foreach(var id in items.Where(p=>p.Value.Expires<=now).Select(p=>p.Key).ToArray())items.Remove(id);
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
    public Item[] List(Guid owner,Guid session,DateTimeOffset now)
    {lock(gate){Prune(now);return items.Values.Where(x=>x.Owner==owner&&(x.Source==session||x.Target==session)).ToArray();}}
    public Item? Decide(Guid owner,Guid session,Guid id,bool approve,DateTimeOffset now)
    {
        lock(gate)
        {
            Prune(now);
            if(!items.TryGetValue(id,out var item)||item.Owner!=owner||item.Target!=session||item.State!="pending")return null;
            var result=item with{State=approve?"approved":"declined"};items[id]=result;return result;
        }
    }
}
