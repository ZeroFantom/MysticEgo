using System.Text.RegularExpressions;

namespace MysticEgo.Distributed;

// Only creates a proposal. Execution is never reachable through SignalR/MCP commands.
public sealed class MobileLaunchQueue
{
    public sealed record Item(Guid Id,string Owner,string Alias,string Path,DateTimeOffset Expires,string State);
    private readonly object gate=new();
    private readonly Dictionary<Guid,Item> rows=new();
    public static bool ValidAlias(string? alias)=>alias is {Length:>0 and <=40}&&Regex.IsMatch(alias,@"\A[A-Za-z0-9_.-]+\z",RegexOptions.CultureInvariant,TimeSpan.FromMilliseconds(50));
    private void Prune(DateTimeOffset now)
    {
        foreach(var id in rows.Where(x=>x.Value.Expires.AddMinutes(8)<=now).Select(x=>x.Key).ToArray())rows.Remove(id);
        foreach(var id in rows.Where(x=>x.Value.Expires<=now&&x.Value.State=="pending").Select(x=>x.Key).ToArray())rows[id]=rows[id] with{State="expired"};
    }
    public Item? Propose(string owner,string alias,string path,DateTimeOffset now)
    {
        if(string.IsNullOrWhiteSpace(owner)||!ValidAlias(alias)||!System.IO.Path.IsPathFullyQualified(path))return null;
        lock(gate){Prune(now);if(rows.Count>=100||rows.Values.Count(x=>x.Owner==owner&&x.State=="pending")>=5)return null;
            var item=new Item(Guid.NewGuid(),owner,alias,path,now.AddMinutes(2),"pending");rows.Add(item.Id,item);return item;}
    }
    public Item[] List(string owner,DateTimeOffset now)
    {lock(gate){Prune(now);return rows.Values.Where(x=>x.Owner==owner).OrderByDescending(x=>x.Expires).Take(20).ToArray();}}
    public Item? Read(string owner,Guid id,DateTimeOffset now)
    {lock(gate){Prune(now);return rows.TryGetValue(id,out var item)&&item.Owner==owner?item:null;}}
    public Item? Decide(string owner,Guid id,bool approve,DateTimeOffset now)
    {
        lock(gate){Prune(now);if(!rows.TryGetValue(id,out var item)||item.Owner!=owner||item.State!="pending")return null;
            var next=item with{State=approve?"claimed":"declined"};rows[id]=next;return next;}
    }
    public Item? Finish(string owner,Guid id,bool started,DateTimeOffset now)
    {
        lock(gate){Prune(now);if(!rows.TryGetValue(id,out var item)||item.Owner!=owner||item.State!="claimed")return null;
            var next=item with{State=started?"launch-requested":"failed"};rows[id]=next;return next;}
    }
}
