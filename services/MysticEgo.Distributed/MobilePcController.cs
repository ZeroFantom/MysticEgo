using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Models.Commands.SignalR;
using Models.Commands.SignalR.Hub;

namespace MysticEgo.Distributed;

[ApiController,Authorize(Policy="operator"),Route("api/v1/mobile-pc/mcp")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MobilePcController(NodeOptions node,INodeAccessPolicy access,IClientRegistry registry,
    ICapabilityStore capabilities,INodeCommandService commands,IConfiguration config):ControllerBase
{
    private static readonly SemaphoreSlim InFlight=new(2);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    private bool Allowed()=>node.Kind==NodeKind.Orchestra&&EcosystemHierarchy.IsDeveloper(User)&&access.CanCommand(User)&&
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var account)&&
        Guid.TryParse(User.FindFirst("mystic_session")?.Value,out var session)&&session!=Guid.Empty&&
        MobilePcPolicy.OwnerAllowed(account,config["MobilePc:OwnerId"]);
    [HttpPost,RequestSizeLimit(4096)]
    public async Task<IActionResult> Post(JsonElement q,CancellationToken ct)
    {
        if(!Allowed())return Forbid();
        if(q.ValueKind!=JsonValueKind.Object||!q.TryGetProperty("jsonrpc",out var version)||version.ValueKind!=JsonValueKind.String||version.GetString()!="2.0"||
            !q.TryGetProperty("method",out var methodValue)||methodValue.ValueKind!=JsonValueKind.String)return BadRequest();
        var method=methodValue.GetString();
        if(!q.TryGetProperty("id",out var id))return method=="notifications/initialized"?Accepted():BadRequest();
        if(id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))return BadRequest();
        IActionResult Result(object result)=>Ok(new{jsonrpc="2.0",id,result});
        IActionResult Error(int code,string message)=>Ok(new{jsonrpc="2.0",id,error=new{code,message}});
        IActionResult Content(object value)=>Result(new{content=new[]{new{type="text",text=JsonSerializer.Serialize(value,Json)}},isError=false});
        object Schema(Dictionary<string,object> properties,string[] required)=>new{type="object",properties,required,additionalProperties=false};
        if(method=="initialize")return Result(new{protocolVersion="2024-11-05",capabilities=new{tools=new{}},serverInfo=new{name="MysticEgo Mobile PC diagnostics",version="0.1"}});
        if(method=="ping")return Result(new{});
        if(method=="tools/list")return Result(new{tools=new object[]{
            new{name="pc.list",description="List explicitly configured stations on this Orchestra.",inputSchema=Schema(new(),[])},
            new{name="pc.diagnose",description="Read limited runtime telemetry or integration availability from an explicitly configured online station.",inputSchema=Schema(new(){["targetId"]=new{type="string",format="uuid"},["action"]=new{type="string",@enum=MobilePcPolicy.Actions}},["targetId","action"])},
            new{name="pc.launch.propose",description="Request local PC owner approval for a locally allowlisted alias. Does not launch.",inputSchema=Schema(new(){["targetId"]=new{type="string",format="uuid"},["alias"]=new{type="string",maxLength=40}},["targetId","alias"])},
            new{name="pc.launch.status",description="Read a short-lived launch proposal status.",inputSchema=Schema(new(){["targetId"]=new{type="string",format="uuid"},["requestId"]=new{type="string",format="uuid"}},["targetId","requestId"])}
        }});
        if(method!="tools/call")return Error(-32601,"Method not found");
        if(!q.TryGetProperty("params",out var p)||p.ValueKind!=JsonValueKind.Object||
            !p.TryGetProperty("name",out var name)||name.ValueKind!=JsonValueKind.String||
            !p.TryGetProperty("arguments",out var args)||args.ValueKind!=JsonValueKind.Object)return Error(-32602,"Invalid arguments");
        var allowed=MobilePcPolicy.Targets(config["MobilePc:AllowedTargets"]);
        if(name.GetString()=="pc.list"&&args.EnumerateObject().Count()==0)
        {
            var rows=(await registry.ListAsync(ct)).Where(r=>allowed.Contains(r.ClientId)).Select(r=>new{id=r.ClientId,name=r.MachineName,online=r.Online,lastSeenUtc=r.LastSeenAt});
            return Content(rows);
        }
        if(name.GetString() is "pc.launch.propose" or "pc.launch.status")
        {
            var propose=name.GetString()=="pc.launch.propose";
            var parameter=propose?"alias":"requestId";
            if(args.EnumerateObject().Count()!=2||!args.TryGetProperty("targetId",out var targetValue)||targetValue.ValueKind!=JsonValueKind.String||!targetValue.TryGetGuid(out var station)||
                !args.TryGetProperty(parameter,out var value)||value.ValueKind!=JsonValueKind.String||
                (propose?!MobileLaunchQueue.ValidAlias(value.GetString()):!Guid.TryParse(value.GetString(),out _)))return Error(-32602,"Invalid arguments");
            var command=propose?"mobile.launch.propose":"mobile.launch.status";
            if(!config.GetValue<bool>("MobilePc:LaunchProposalsEnabled")||!allowed.Contains(station)||!access.SensitiveAllowed(User,command,false)||
                await registry.GetByIdAsync(station,ct) is not {Online:true}||!(await capabilities.GetAsync(station,ct)).Any(c=>c.Id.Value==command))return Error(-32602,"Target or proposal unavailable");
            if(!await InFlight.WaitAsync(0,ct))return Error(-32000,"Request limit reached");
            try{
                if(await commands.Dispatch(new(station,command,value.GetString(),null,15,false),User.Identity?.Name,ct) is not CommandExecutionResult execution||!execution.Success||execution.StructuredOutput is null)return Error(-32000,"Proposal unavailable");
                var output=JsonSerializer.SerializeToElement(execution.StructuredOutput,Json);
                if(output.ValueKind!=JsonValueKind.Object||!output.TryGetProperty("id",out var rid)||rid.ValueKind!=JsonValueKind.String||!rid.TryGetGuid(out var requestId)||
                    !output.TryGetProperty("alias",out var alias)||alias.ValueKind!=JsonValueKind.String||!MobileLaunchQueue.ValidAlias(alias.GetString())||
                    !output.TryGetProperty("state",out var status)||status.ValueKind!=JsonValueKind.String||status.GetString() is not ("pending" or "claimed" or "declined" or "expired" or "launch-requested" or "failed")||
                    !output.TryGetProperty("expires",out var expires)||expires.ValueKind!=JsonValueKind.String||!expires.TryGetDateTimeOffset(out var expiration))return Error(-32000,"Invalid proposal response");
                return Content(new{targetId=station,id=requestId,alias=alias.GetString(),state=status.GetString(),expires=expiration});
            }catch(Exception ex) when(ex is NodeApiException or InvalidOperationException or JsonException or TimeoutException or HttpRequestException){return Error(-32000,"Proposal unavailable");}
            finally{InFlight.Release();}
        }
        if(name.GetString()!="pc.diagnose"||args.EnumerateObject().Count()!=2||
            !args.TryGetProperty("targetId",out var t)||t.ValueKind!=JsonValueKind.String||!t.TryGetGuid(out var target)||
            !args.TryGetProperty("action",out var a)||a.ValueKind!=JsonValueKind.String||!MobilePcPolicy.ActionAllowed(a.GetString()))
            return Error(-32602,"Invalid arguments");
        var action=a.GetString()!;
        if(!allowed.Contains(target)||!access.SensitiveAllowed(User,action,false)||
            await registry.GetByIdAsync(target,ct) is not {Online:true}||
            !(await capabilities.GetAsync(target,ct)).Any(c=>c.Id.Value==action))return Error(-32602,"Target or action unavailable");
        if(!await InFlight.WaitAsync(0,ct))return Error(-32000,"Diagnostic limit reached");
        try
        {
            // The existing dispatcher records the account, target and outcome; never dispatch arbitrary arguments.
            if(await commands.Dispatch(new(target,action,"",null,15,false),User.Identity?.Name,ct) is not CommandExecutionResult result||
                !result.Success||result.StructuredOutput is null)return Error(-32000,"Diagnostic unavailable");
            var filtered=MobilePcPolicy.Filter(action,JsonSerializer.SerializeToElement(result.StructuredOutput,Json));
            return filtered.Count==0?Error(-32000,"No supported telemetry returned"):Content(new{targetId=target,action,executionId=result.ExecutionId,data=filtered});
        }
        catch(Exception error) when(error is NodeApiException or InvalidOperationException or JsonException or TimeoutException or HttpRequestException)
        {return Error(-32000,"Diagnostic unavailable");}
        finally{InFlight.Release();}
    }
}
