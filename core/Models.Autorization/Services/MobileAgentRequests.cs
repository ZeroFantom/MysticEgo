using System.Security.Cryptography;
using System.Text;

namespace Models.Autorization.Services;

/// <summary>Foreground-only native agent requests. Registration is durable; tickets and actions are deliberately ephemeral.</summary>
public sealed class MobileAgentRequests
{
    public static readonly string[] Actions=["battery-status","vibrate","open-assistant"];
    public sealed record Ticket(Guid Owner,Guid Session,DateTimeOffset Expires);
    public sealed record Item(Guid Id,Guid Owner,Guid Source,Guid Target,string Action,DateTimeOffset Expires,string State,string? Result=null);
    private readonly object gate=new();
    private readonly Dictionary<string,Ticket> tickets=new(StringComparer.Ordinal);
    private readonly Dictionary<Guid,Item> items=new();
    public static string Secret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool ValidSecret(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Match(string hash,string? value)=>ValidSecret(value)&&hash.Length==64&&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash),Encoding.ASCII.GetBytes(Hash(value!)));
    private void Prune(DateTimeOffset now)
    {
        foreach(var k in tickets.Where(x=>x.Value.Expires<=now).Select(x=>x.Key).ToArray())tickets.Remove(k);
        foreach(var k in items.Where(x=>x.Value.Expires<=now).Select(x=>x.Key).ToArray())items.Remove(k);
    }
    public string? Issue(Guid owner,Guid session,DateTimeOffset now)
    {
        if(owner==Guid.Empty||session==Guid.Empty)return null;
        lock(gate){
            Prune(now);
            if(tickets.Count>=500)return null;
            foreach(var old in tickets.Where(x=>x.Value.Owner==owner&&x.Value.Session==session).Select(x=>x.Key).ToArray())tickets.Remove(old);
            var code=Secret();tickets.Add(Hash(code),new(owner,session,now.AddMinutes(2)));return code;
        }
    }
    public Ticket? Consume(string? code,DateTimeOffset now)
    {
        if(!ValidSecret(code))return null;
        lock(gate){Prune(now);return tickets.Remove(Hash(code!),out var ticket)?ticket:null;}
    }
    public Item? Request(Guid owner,Guid source,Guid target,string action,DateTimeOffset now)
    {
        if(owner==Guid.Empty||source==Guid.Empty||target==Guid.Empty||!Actions.Contains(action,StringComparer.Ordinal))return null;
        lock(gate){
            Prune(now);if(items.Count>=500||items.Values.Count(x=>x.Owner==owner)>=20)return null;
            var item=new Item(Guid.NewGuid(),owner,source,target,action,now.AddMinutes(2),"pending");items.Add(item.Id,item);return item;
        }
    }
    public Item[] ForSource(Guid owner,Guid source,DateTimeOffset now)
    {lock(gate){Prune(now);return items.Values.Where(x=>x.Owner==owner&&x.Source==source).ToArray();}}
    public Item[] ForTarget(Guid owner,Guid target,DateTimeOffset now)
    {lock(gate){Prune(now);return items.Values.Where(x=>x.Owner==owner&&x.Target==target&&x.State=="pending").ToArray();}}
    public Item? Decide(Guid owner,Guid target,Guid id,bool approve,DateTimeOffset now)
    {
        lock(gate){Prune(now);if(!items.TryGetValue(id,out var item)||item.Owner!=owner||item.Target!=target||item.State!="pending")return null;
            var result=item with{State=approve?"accepted":"declined"};items[id]=result;return result;}
    }
    public Item? Complete(Guid owner,Guid target,Guid id,bool success,string? result,DateTimeOffset now)
    {
        lock(gate){
            Prune(now);if(!items.TryGetValue(id,out var item)||item.Owner!=owner||item.Target!=target||item.State!="accepted")return null;
            // Only known result values; no arbitrary text, files, logs or sensitive payloads.
            if(success&&(item.Action=="battery-status"? !int.TryParse(result,out var battery)||battery<0||battery>100 : result!="ok"))return null;
            var updated=item with{State=success?"reported-success":"reported-failure",Result=success?result:null};items[id]=updated;return updated;
        }
    }
    public void Revoke(Guid owner,Guid target)
    {lock(gate){foreach(var k in items.Where(x=>x.Value.Owner==owner&&x.Value.Target==target).Select(x=>x.Key).ToArray())items.Remove(k);}}
}
