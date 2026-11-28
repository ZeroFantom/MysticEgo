using System.Security.Cryptography;
namespace Models.Autorization.Services;

/// <summary>In-memory bounded rendezvous. The QR carries only a public request ID, never a verifier, grant or account token.</summary>
public sealed class QrSignIn
{
    public sealed record View(string Id,string Comparison,DateTimeOffset Expires,SessionDevice Device);
    private sealed record Entry(View View,string Challenge,string? Grant=null);
    private readonly Dictionary<string,Entry> pending=new(StringComparer.Ordinal);
    private readonly object gate=new();
    private static string RandomSecret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
    private void Prune(DateTimeOffset now)
    {
        foreach(var id in pending.Where(x=>x.Value.View.Expires<=now).Select(x=>x.Key).ToArray())pending.Remove(id);
    }
    public View? Start(string? challenge,string? ua,DateTimeOffset now)
    {
        if(!DesktopPairing.ValidSecret(challenge))return null;
        lock(gate)
        {
            Prune(now);
            if(pending.Count>=500)return null;
            var id=RandomSecret();
            var view=new View(id,RandomNumberGenerator.GetInt32(100000,1000000).ToString(),now.AddMinutes(2),SessionDevice.Describe(ua));
            pending.Add(id,new(view,challenge!));return view;
        }
    }
    public View? Inspect(string? id,DateTimeOffset now)
    {
        if(!DesktopPairing.ValidSecret(id))return null;
        lock(gate){Prune(now);return pending.TryGetValue(id!,out var e)&&e.Grant is null?e.View:null;}
    }
    public bool Approve(string? id,string? comparison,Guid user,string sourceHash,DesktopPairing pairing,DateTimeOffset now)
    {
        if(!DesktopPairing.ValidSecret(id)||comparison is null)return false;
        lock(gate)
        {
            Prune(now);
            if(!pending.TryGetValue(id!,out var e)||e.Grant is not null||e.View.Comparison!=comparison)return false;
            var grant=pairing.Create(user,sourceHash,e.Challenge,now);
            if(grant is null)return false;
            pending[id!]=e with{Grant=grant};return true;
        }
    }
    public (string State,string? Grant) Redeem(string? id,string? verifier,DateTimeOffset now)
    {
        if(!DesktopPairing.ValidSecret(id)||!DesktopPairing.ValidSecret(verifier))return ("invalid",null);
        lock(gate)
        {
            Prune(now);
            if(!pending.TryGetValue(id!,out var e)||!Matches(e,verifier!))return ("invalid",null);
            if(e.Grant is null)return ("pending",null);
            pending.Remove(id!);return ("approved",e.Grant);
        }
    }
    public void Cancel(string? id,string? verifier,DateTimeOffset now)
    {
        if(!DesktopPairing.ValidSecret(id)||!DesktopPairing.ValidSecret(verifier))return;
        lock(gate){Prune(now);if(pending.TryGetValue(id!,out var e)&&Matches(e,verifier!))pending.Remove(id!);}
    }
    private static bool Matches(Entry e,string verifier)=>CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(e.Challenge),Convert.FromHexString(DesktopPairing.Hash(verifier)));
}
