using System.Text.Json;

namespace MysticEgo.Distributed;

// Opt-in pilot for a single configured account and explicitly listed station IDs.
// No user-supplied addresses, arbitrary arguments, or write actions.
public static class MobilePcPolicy
{
    public static readonly string[] Actions=["system.health","integrations.status"];
    public static bool OwnerAllowed(Guid account,string? configured)=>
        account!=Guid.Empty&&Guid.TryParse(configured,out var owner)&&account==owner;
    public static HashSet<Guid> Targets(string? configured)=>
        (configured??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
            .Take(64).Select(s=>Guid.TryParse(s,out var id)?id:Guid.Empty).Where(x=>x!=Guid.Empty).ToHashSet();
    public static bool ActionAllowed(string? action)=>Actions.Contains(action,StringComparer.Ordinal);
    public static Dictionary<string,object> Filter(string action,JsonElement value)
    {
        var result=new Dictionary<string,object>(StringComparer.Ordinal);
        if(value.ValueKind!=JsonValueKind.Object||!ActionAllowed(action))return result;
        if(action=="system.health")
        {
            foreach(var key in new[]{"uptimeMs","managedHeapBytes","agentWorkingSetBytes","agentThreads"})
                if(value.TryGetProperty(key,out var item)&&item.ValueKind==JsonValueKind.Number&&item.TryGetInt64(out var count)&&count>=0)
                    result[key]=count;
        }
        else
        {
            foreach(var key in new[]{"openRgb","logitechGHub","msiCenter","nvidia","lmStudioApi"})
                if(value.TryGetProperty(key,out var item)&&item.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    result[key]=item.GetBoolean();
        }
        return result;
    }
}
