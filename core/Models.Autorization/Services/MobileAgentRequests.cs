using System.Security.Cryptography;
using System.Text;

namespace Models.Autorization.Services;

/// <summary>Single-use, short-lived enrollment tickets. Action states are persisted separately.</summary>
public sealed class MobileAgentRequests
{
    public static readonly string[] Actions=["battery-status","vibrate","open-assistant"];
    public sealed record Ticket(Guid Owner,Guid Session,DateTimeOffset Expires);
    private readonly object gate=new();
    private readonly Dictionary<string,Ticket> tickets=new(StringComparer.Ordinal);
    public static string Secret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    public static bool ValidSecret(string? value)=>value is {Length:64}&&value.All(c=>c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool Match(string hash,string? value)=>ValidSecret(value)&&hash.Length==64&&
        CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash),Encoding.ASCII.GetBytes(Hash(value!)));
    private void Prune(DateTimeOffset now)
    {
        foreach(var k in tickets.Where(x=>x.Value.Expires<=now).Select(x=>x.Key).ToArray())tickets.Remove(k);
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
}
