using System.Text.Json;
using System.Text.RegularExpressions;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

public interface INativeDeploymentPlanService
{
    string[] List();
    Task<string?> Read(string id,CancellationToken ct);
    Task<object> Save(string id,NativePlan payload,CancellationToken ct);
    void Delete(string id);
}
public sealed class NativeDeploymentPlanService(IConfiguration config) : INativeDeploymentPlanService
{
    private static readonly Regex PlanId=new("^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$",RegexOptions.Compiled);
    private static readonly Regex Host=new("^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$",RegexOptions.Compiled);
    private static readonly HashSet<string> Roles=new(StringComparer.OrdinalIgnoreCase){"auth","orchestra","global","local","agent","audit"};
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private string Folder()=>Path.GetFullPath(config["Orchestra:NativePlansPath"]??Path.Combine(AppContext.BaseDirectory,"data","native-plans"));
    private static void CheckId(string id){if(!PlanId.IsMatch(id))throw new NodeApiException(400,"Invalid plan id");}
    public string[] List(){var folder=Folder();Directory.CreateDirectory(folder);return Directory.EnumerateFiles(folder,"*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(x=>x).ToArray();}
    public async Task<string?> Read(string id,CancellationToken ct)
    {
        CheckId(id);var file=Path.Combine(Folder(),id+".json");
        return File.Exists(file)?await File.ReadAllTextAsync(file,ct):null;
    }
    public async Task<object> Save(string id,NativePlan payload,CancellationToken ct)
    {
        CheckId(id);var error=Validate(payload);
        if(error is not null)throw new NodeApiException(400,error);
        var folder=Folder();Directory.CreateDirectory(folder);
        var target=Path.Combine(folder,id+".json");var tmp=target+"."+Guid.NewGuid().ToString("N")+".tmp";
        await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(payload,Json),ct);
        File.Move(tmp,target,true);
        return new{id,nodes=payload.Nodes.Length,mode="native",notice="Plan saved; only the trusted installer may execute it."};
    }
    public void Delete(string id)
    {
        CheckId(id);var file=Path.Combine(Folder(),id+".json");
        if(!File.Exists(file))throw new NodeApiException(404,"Plan not found");
        File.Delete(file);
    }
    private static string? Validate(NativePlan? p)
    {
        if(p?.Nodes==null || p.Nodes.Length==0 || p.Nodes.Length>100)return "Expected 1-100 nodes";
        if(p.Nodes.GroupBy(n=>n.Host,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))return "Duplicate host";
        foreach(var n in p.Nodes)
        {
            if(n.Host!="local" && !Host.IsMatch(n.Host))return "Invalid SSH host";
            if(n.Roles==null || n.Roles.Length==0 || n.Roles.Any(r=>!Roles.Contains(r)))return "Unsupported role";
            if(n.Roles.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=n.Roles.Length)return "Duplicate role";
            bool Has(string role,string? url)=>n.Roles.Contains(role,StringComparer.OrdinalIgnoreCase)||!string.IsNullOrEmpty(url);
            if(!Has("auth",n.AuthUrl))return "Missing Auth";
            if(n.Roles.Contains("global",StringComparer.OrdinalIgnoreCase)&&!Has("orchestra",n.OrchestraUrl))return "Global requires Orchestra";
            if(n.Roles.Contains("local",StringComparer.OrdinalIgnoreCase)&&!Has("global",n.GlobalUrl)&&!Has("audit",n.AuditUrl))return "Local requires Global/Audit";
            if(n.Roles.Contains("agent",StringComparer.OrdinalIgnoreCase)&&!Has("local",n.LocalUrl)&&!Has("audit",n.AuditUrl))return "Agent requires Local/Audit";
            foreach(var url in new[]{n.AuthUrl,n.OrchestraUrl,n.GlobalUrl,n.LocalUrl,n.AuditUrl}.Where(x=>!string.IsNullOrWhiteSpace(x)))
                if(!Uri.TryCreate(url,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo.Length>0||u.Fragment.Length>0)return "Upstream URL must use HTTPS";
        }
        return null;
    }
}
public sealed record NativePlan(NativeTarget[] Nodes);
public sealed record NativeTarget(string Host,string[] Roles,string? AuthUrl=null,string? OrchestraUrl=null,string? GlobalUrl=null,string? LocalUrl=null,string? AuditUrl=null);
