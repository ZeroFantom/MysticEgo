using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using Models.Commands;
using Models.Commands.SignalR;
using Models.Commands.SignalR.Hub;
using Models.Data;

namespace MysticEgo.Distributed;

public interface INodeAccessPolicy
{
    bool Operator(ClaimsPrincipal principal);
    bool Admin(ClaimsPrincipal principal);
    bool CanCommand(ClaimsPrincipal principal);
    bool Peer(ClaimsPrincipal principal);
    bool SensitiveAllowed(ClaimsPrincipal principal, string commandId, bool confirmed);
}
public sealed class NodeAccessPolicy(NodeOptions options) : INodeAccessPolicy
{
    private static readonly Dictionary<string, CommandRiskAttribute> Risks =
        typeof(SystemInfoCommand).Assembly.GetTypes()
            .Where(CommandMetadata.IsCommand)
            .Select(type => new
            {
                Id = CommandMetadata.GetId(type).Value,
                Risk = type.GetCustomAttributes(typeof(CommandRiskAttribute), false).Cast<CommandRiskAttribute>().FirstOrDefault()
            })
            .Where(x => x.Risk != null)
            .ToDictionary(x => x.Id, x => x.Risk!, StringComparer.OrdinalIgnoreCase);
    private static bool Permission(ClaimsPrincipal user, string permission) => user.HasClaim("permission", permission);
    private bool IsDeviceOwner(ClaimsPrincipal user) => options.Kind is NodeKind.Agent or NodeKind.AuditOrchestra &&
        Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER") is { Length: > 0 } owner &&
        user.Identity?.Name == owner;
    public bool Operator(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true &&
        user.FindFirst("token_use")?.Value != "device" &&
        (user.HasClaim(c => c.Type == "role_code") || Permission(user, "node.read") || IsDeviceOwner(user));
    public bool Admin(ClaimsPrincipal user) => Operator(user) &&
        (user.IsInRole("system.developer") || Permission(user,"system.bypass") || Permission(user,"node.write"));
    public bool CanCommand(ClaimsPrincipal user) => Admin(user) || (Operator(user) && (Permission(user,"node.command") || IsDeviceOwner(user)));
    public bool Peer(ClaimsPrincipal user) => user.FindFirst("token_use")?.Value == "device" &&
        (user.IsInRole("local") || user.IsInRole("global") || user.IsInRole("agent") || user.IsInRole("audit"));
    public bool SensitiveAllowed(ClaimsPrincipal user,string commandId,bool confirmed) =>
        CommandId.TryParse(commandId,out var canonical) && (!Risks.TryGetValue(canonical.Value,out var risk) ||
        (!risk.RequiresHumanApproval || confirmed) &&
        (risk.Risk != CommandRisk.Privileged || user.IsInRole("system.developer") || Permission(user,"system.bypass") ||
            IsDeviceOwner(user) && canonical.Value is "automation.play" or "automation.stop" ||
            Permission(user, "node.command") && canonical.Value is "automation.play" or "automation.stop"));
}

public sealed class NodeApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface INodeQueryService
{
    object Deployment();
    object Ping();
    Task<object> Links(CancellationToken ct);
    object Meta();
    Task<object> Overview(CancellationToken ct);
    object Nodes();
    object Announce(NodeAnnounce announce);
    Task<object> Agents();
    Task<object> Capabilities(Guid agentId, CancellationToken ct);
    object Audit();
}

public sealed class NodeQueryService(NodeOptions options,NodeState state,IHttpClientFactory factory,
    IClientRegistry clients,ICapabilityStore capabilities,CommandCatalog catalog) : INodeQueryService
{
    private static readonly HashSet<string> Collections = new(StringComparer.OrdinalIgnoreCase)
    { "clubs", "zones", "presets", "automations", "configs", "langame-links", "langame-logs", "devices", "applications", "sessions", "profiles", "alerts" };
    public object Deployment() => new { mode=options.DeploymentMode,
        label=options.DeploymentMode switch { "single-machine" => "Одна машина", "shared-control-plane" => "Совмещённый центр управления", "remote-distributed" => "Удалённая распределённая система", _ => "Неизвестно" },
        description=options.DeploymentDescription,node=options.NodeId,kind=options.Kind.ToString(),auth=options.AuthUrl,global=options.GlobalUrl,local=options.LocalUrl };
    public object Ping() => new { node=options.NodeId,serverUtc=DateTimeOffset.UtcNow,monotonicMs=Environment.TickCount64 };
    public async Task<object> Links(CancellationToken ct)
    {
        var links=new[] { ("auth",options.AuthUrl,"/health"),("global",options.GlobalUrl,"/health/live"),("local",options.LocalUrl,"/health/live") };
        var results=new List<object>();
        foreach(var (name,url,path) in links)
        {
            if(string.IsNullOrWhiteSpace(url)) continue;
            var watch=Stopwatch.StartNew();
            try
            {
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
                using var response=await factory.CreateClient().GetAsync(url.TrimEnd('/')+path,timeout.Token);
                results.Add(new {name,url,status=(int)response.StatusCode,ms=watch.Elapsed.TotalMilliseconds,reachable=response.IsSuccessStatusCode});
            }
            catch(Exception e) when(e is HttpRequestException || e is OperationCanceledException && !ct.IsCancellationRequested)
            {
                results.Add(new {name,url,status=0,ms=watch.Elapsed.TotalMilliseconds,reachable=false});
            }
        }
        return results;
    }
    public object Meta() => new {id=options.NodeId,name=options.DisplayName,kind=options.Kind.ToString(),version="0.3.0",features=Collections.Order().ToArray(),transports=new[]{"http","signalr"}};
    public async Task<object> Overview(CancellationToken ct) => new
    {
        node=options.NodeId,kind=options.Kind.ToString(),agentsOnline=await clients.CountOnlineAsync(),
        nodes=state.Nodes.Count,records=(await state.Data.ListAsync<RecordDocument>()).Count,at=DateTimeOffset.UtcNow
    };
    public object Nodes() => state.Nodes.Values.OrderBy(x=>x.Name).ToArray();
    public object Announce(NodeAnnounce announce)
    {
        if(string.IsNullOrWhiteSpace(announce.Id)||announce.Id.Length>128)throw new NodeApiException(400,"Invalid node identity");
        state.Nodes[announce.Id]=announce with {SeenAt=DateTimeOffset.UtcNow};
        return new {accepted=true};
    }
    public async Task<object> Agents()
    {
        if (Guid.TryParse(options.IdentityId, out var self) && options.Kind is NodeKind.Agent or NodeKind.AuditOrchestra)
            return new[] { new { clientId = self, online = true, host = Environment.MachineName,
                capabilityCount = (await catalog.Read(default)).GetArrayLength() } };
        return await clients.ListAsync();
    }
    public async Task<object> Capabilities(Guid agentId,CancellationToken ct)
    {
        var caps=await capabilities.GetAsync(agentId,ct);
        if (Guid.TryParse(options.IdentityId, out var self) && self == agentId && options.Kind is NodeKind.Agent or NodeKind.AuditOrchestra)
            return await catalog.Read(ct);
        return caps.Select(CommandCatalog.Describe).ToArray();
    }
    public object Audit() => state.Audit.ToArray().Reverse().Take(200).ToArray();
}

public interface INodeRecordService
{
    Task<object> List(string collection,CancellationToken ct);
    Task<RecordDocument> Create(string collection,SaveRecord record,string actor,CancellationToken ct);
    Task<RecordDocument> Update(string collection,Guid id,SaveRecord record,string actor,CancellationToken ct);
    Task Delete(string collection,Guid id,string actor,CancellationToken ct);
}

public sealed class NodeRecordService(NodeState state) : INodeRecordService
{
    private static readonly HashSet<string> Collections=new(StringComparer.OrdinalIgnoreCase)
    { "clubs", "zones", "presets", "automations", "configs", "langame-links", "langame-logs", "devices", "applications", "sessions", "profiles", "alerts" };
    private static void CheckCollection(string collection)
    { if(!Collections.Contains(collection))throw new NodeApiException(404,"Unknown collection"); }
    private static void CheckRecord(SaveRecord input)
    { if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>160||input.Data.ValueKind==JsonValueKind.Undefined)throw new NodeApiException(400,"Invalid record"); }
    public async Task<object> List(string collection,CancellationToken ct)
    {
        CheckCollection(collection);
        var all=await state.Data.ListAsync<RecordDocument>(2000,ct);
        return all.Where(x=>x.Collection.Equals(collection,StringComparison.OrdinalIgnoreCase)).ToArray();
    }
    public async Task<RecordDocument> Create(string collection,SaveRecord record,string actor,CancellationToken ct)
    {
        CheckCollection(collection);CheckRecord(record);
        var document=new RecordDocument{Id=Guid.NewGuid(),Collection=collection,Name=record.Name,Data=record.Data.Clone()};
        await state.Data.PutAsync(document,ct);state.Log(actor,"create",collection,true);
        return document;
    }
    public async Task<RecordDocument> Update(string collection,Guid id,SaveRecord record,string actor,CancellationToken ct)
    {
        CheckCollection(collection);CheckRecord(record);
        var existing=await state.Data.GetAsync<RecordDocument>(id,ct);
        if(existing is null || existing.Collection!=collection)throw new NodeApiException(404,"Record not found");
        existing.Name=record.Name;existing.Data=record.Data.Clone();existing.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(existing,ct);state.Log(actor,"update",collection,true);
        return existing;
    }
    public async Task Delete(string collection,Guid id,string actor,CancellationToken ct)
    {
        CheckCollection(collection);
        var existing=await state.Data.GetAsync<RecordDocument>(id,ct);
        if(existing is null || existing.Collection!=collection)throw new NodeApiException(404,"Record not found");
        await state.Data.DeleteAsync<RecordDocument>(id,ct);state.Log(actor,"delete",collection,true);
    }
}

public interface INodeCommandService
{
    Task<object> Dispatch(CommandRequest command,string? actor,CancellationToken ct);
    Task<object> Bulk(BulkCommandRequest command,string? actor,CancellationToken ct);
}
public sealed class NodeCommandService(NodeState state,IExecutionDispatcher dispatcher,IServiceProvider services) : INodeCommandService
{
    public async Task<object> Dispatch(CommandRequest command,string? actor,CancellationToken ct)
    {
        if(command.TargetId==Guid.Empty || command.TimeoutSeconds is <1 or >120)throw new NodeApiException(400,"invalid_target_or_timeout");
        CommandId id;
        try {id=CommandId.FromName(command.CommandId);} catch {throw new NodeApiException(400,"invalid_command");}
        var req=new CommandExecutionRequest {ExecutionId=Guid.NewGuid(),CommandId=id,RawArgs=command.RawArgs??"",
            NamedArgs=command.NamedArgs?.ToDictionary(k=>k.Key,k=>(object?)k.Value),CallerLogin=actor,TimeoutSeconds=command.TimeoutSeconds};
        CommandExecutionResult result;
        var node = state.Options;
        if (Guid.TryParse(node.IdentityId, out var self) && self == command.TargetId && node.Kind is NodeKind.Agent or NodeKind.AuditOrchestra &&
            services.GetService<Models.Commands.SignalR.Clients.ICommandInvoker>() is { } invoker)
        {
            using var time = CancellationTokenSource.CreateLinkedTokenSource(ct);
            time.CancelAfter(TimeSpan.FromSeconds(command.TimeoutSeconds));
            var clock = Stopwatch.StartNew();
            var output = await invoker.InvokeAsync(new CommandContext
            {
                ExecutionId = req.ExecutionId, CommandId = id, RawArgs = req.RawArgs, NamedArgs = req.NamedArgs,
                CallerLogin = actor
            }, time.Token);
            result = output.Success
                ? CommandExecutionResult.Ok(req.ExecutionId, id, output.Output, clock.Elapsed, output.StructuredOutput)
                : CommandExecutionResult.Fail(req.ExecutionId, id, output.ErrorCode ?? "command_failed", output.ErrorMessage ?? "", clock.Elapsed);
        }
        else result = await dispatcher.DispatchAsync(command.TargetId, req, TimeSpan.FromSeconds(command.TimeoutSeconds + 5), ct: ct);
        state.Log(actor??"unknown","command",command.TargetId.ToString(),result.Success,command.CommandId);
        return result;
    }
    public async Task<object> Bulk(BulkCommandRequest bulk,string? actor,CancellationToken ct)
    {
        if(bulk.TargetIds is null || bulk.TargetIds.Length is <1 or >25 || bulk.TargetIds.Distinct().Count()!=bulk.TargetIds.Length ||
            bulk.TimeoutSeconds is <1 or >60)throw new NodeApiException(400,"invalid_bulk_request");
        CommandId commandId;
        try{commandId=CommandId.FromName(bulk.CommandId);} catch{throw new NodeApiException(400,"invalid_command");}
        var tasks=bulk.TargetIds.Select(async id=>
        {
            try
            {
                var request=new CommandExecutionRequest {ExecutionId=Guid.NewGuid(),CommandId=commandId,RawArgs=bulk.RawArgs??"",CallerLogin=actor,TimeoutSeconds=bulk.TimeoutSeconds};
                var result=await dispatcher.DispatchAsync(id,request,TimeSpan.FromSeconds(bulk.TimeoutSeconds+5),ct:ct);
                state.Log(actor??"unknown","bulk",id.ToString(),result.Success,bulk.CommandId);
                return new {target=id,result};
            }
            catch(Exception e){state.Log("bulk","failure",id.ToString(),false,e.Message);return new {target=id,result=(CommandExecutionResult?)null};}
        });
        return await Task.WhenAll(tasks);
    }
}

public static class NodeApiDependencyInjection
{
    public static IServiceCollection AddNodeApiServices(this IServiceCollection services)
    {
        services.AddSingleton<INodeAccessPolicy,NodeAccessPolicy>();
        services.AddScoped<INodeQueryService,NodeQueryService>();
        services.AddScoped<INodeRecordService,NodeRecordService>();
        services.AddScoped<INodeCommandService,NodeCommandService>();
        return services;
    }
}
