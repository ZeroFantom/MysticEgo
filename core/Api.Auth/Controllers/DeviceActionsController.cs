using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

[ApiController,Authorize,SkipPermission,Route("api/auth/device-actions")]
[EnableRateLimiting("auth-sensitive"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DeviceActionsController(AuthDbContext db,DeviceActionQueue queue):BaseApiController
{
    private Guid? Sid=>Guid.TryParse(User.FindFirst("mystic_session")?.Value,out var id)?id:null;
    private bool Account=>CurrentUserId.HasValue&&Sid.HasValue&&!User.HasClaim("token_use","device");
    private Task<bool> Active(Guid owner,Guid session,CancellationToken ct)=>db.RefreshTokens.AsNoTracking().AnyAsync(
        x=>x.UserId==owner&&x.SessionId==session&&x.ExpiresAt>DateTime.UtcNow&&x.User!.IsActive,ct);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if(!Account||!await Active(CurrentUserId!.Value,Sid!.Value,ct))return Unauthorized();
        return Ok(queue.List(CurrentUserId.Value,Sid.Value,DateTimeOffset.UtcNow).Select(x=>new{x.Id,x.Source,x.Target,x.Action,x.Expires,x.State,x.Avatar,incoming=x.Target==Sid.Value}));
    }

    public sealed record ReceiveState(bool Enabled);
    [HttpPost("presence"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Presence(ReceiveState input,CancellationToken ct)
    {
        if(!Account||!await Active(CurrentUserId!.Value,Sid!.Value,ct))return Unauthorized();
        return queue.Presence(CurrentUserId.Value,Sid.Value,input.Enabled,DateTimeOffset.UtcNow)?Ok(new{enabled=input.Enabled}):StatusCode(429);
    }

    public sealed record CancelInput(Guid Id);
    [HttpPost("cancel"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Cancel(CancelInput input,CancellationToken ct)
    {
        if(!Account||!await Active(CurrentUserId!.Value,Sid!.Value,ct))return Unauthorized();
        var item=queue.Cancel(CurrentUserId.Value,Sid.Value,input.Id,DateTimeOffset.UtcNow);
        return item is null?Conflict():Ok(new{item.Id,item.State});
    }

    public sealed record Decision(Guid Id,bool Approve);
    [HttpPost("decide"),RequestSizeLimit(4096)]
    public async Task<IActionResult> Decide(Decision input,CancellationToken ct)
    {
        if(!Account||!await Active(CurrentUserId!.Value,Sid!.Value,ct))return Unauthorized();
        var owner=CurrentUserId.Value;var session=Sid.Value;
        var pending=queue.List(owner,session,DateTimeOffset.UtcNow).FirstOrDefault(x=>x.Id==input.Id&&x.Target==session&&x.State=="pending");
        if(pending is null)return NotFound();
        if(input.Approve&&(!queue.Ready(owner,DateTimeOffset.UtcNow).Contains(session)||!await Active(owner,pending.Source,ct)))return Conflict();
        var result=queue.Decide(owner,session,input.Id,input.Approve,DateTimeOffset.UtcNow);
        return result is null?Conflict():Ok(new{result.Id,result.Action,result.State,avatar=input.Approve?result.Avatar:null});
    }

    // A narrowly scoped authenticated MCP endpoint. No shell, file, sensor, or arbitrary URI actions.
    [HttpPost("mcp"),RequestSizeLimit(8192)]
    public async Task<IActionResult> Mcp(JsonElement request,CancellationToken ct)
    {
        if(!Account||!await Active(CurrentUserId!.Value,Sid!.Value,ct))return Unauthorized();
        if(request.ValueKind!=JsonValueKind.Object||!request.TryGetProperty("jsonrpc",out var version)||version.ValueKind!=JsonValueKind.String||version.GetString()!="2.0"||
            !request.TryGetProperty("method",out var method)||method.ValueKind!=JsonValueKind.String)return BadRequest();
        var name=method.GetString();
        if(!request.TryGetProperty("id",out var id))
            return name=="notifications/initialized"?Accepted():BadRequest();
        if(id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))return BadRequest();
        IActionResult Result(object value)=>Ok(new{jsonrpc="2.0",id,result=value});
        IActionResult Error(int code,string message)=>Ok(new{jsonrpc="2.0",id,error=new{code,message}});
        if(name=="initialize")return Result(new{protocolVersion="2024-11-05",capabilities=new{tools=new{}},serverInfo=new{name="MysticEgo Device Requests",version="0.1"}});
        if(name=="ping")return Result(new{});
        object Schema(Dictionary<string,object> properties,string[] required)=>new{type="object",properties,required,additionalProperties=false};
        var idSchema=new{type="string",format="uuid"};
        if(name=="tools/list")return Result(new{tools=new object[]{
            new{name="device.list",description="List other active sessions belonging to the authenticated account and their foreground receive readiness.",inputSchema=Schema(new(),[])},
            new{name="device.request",description="Request a visible navigation action in another receiving session. Requires confirmation there.",inputSchema=Schema(new(){["targetId"]=idSchema,["action"]=new{type="string",@enum=DeviceActionQueue.Allowed}},["targetId","action"])},
            new{name="avatar.handoff",description="Offer bounded visual avatar state to another receiving session. Requires confirmation there.",inputSchema=Schema(new(){["targetId"]=idSchema,["motion"]=new{type="string",@enum=new[]{"idle","wave","nod","walk","sit","dance"}},["mood"]=new{type="string",@enum=new[]{"neutral","happy","sad","angry","surprised"}},["phaseSeconds"]=new{type="number",minimum=0,maximum=3600}},["targetId","motion","mood","phaseSeconds"])},
            new{name="device.status",description="Read status of an unexpired request belonging to this session. Approved means accepted, not verified execution.",inputSchema=Schema(new(){["requestId"]=idSchema},["requestId"])},
            new{name="device.cancel",description="Cancel a pending request created by the current session.",inputSchema=Schema(new(){["requestId"]=idSchema},["requestId"])}
        }});
        if(name!="tools/call")return Error(-32601,"Method not found");
        if(!request.TryGetProperty("params",out var p)||p.ValueKind!=JsonValueKind.Object||!p.TryGetProperty("name",out var tool)||tool.ValueKind!=JsonValueKind.String||
            !p.TryGetProperty("arguments",out var args)||args.ValueKind!=JsonValueKind.Object)return Error(-32602,"Invalid tool arguments");
        var owner=CurrentUserId.Value;var source=Sid.Value;var now=DateTimeOffset.UtcNow;
        IActionResult Content(object value)=>Result(new{content=new[]{new{type="text",text=JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web))}},isError=false});
        if(tool.GetString()=="device.list"&&args.EnumerateObject().Count()==0)
        {
            var ready=queue.Ready(owner,now);
            var active=await db.RefreshTokens.AsNoTracking().Where(x=>x.UserId==owner&&x.SessionId!=null&&x.SessionId!=source&&x.ExpiresAt>DateTime.UtcNow&&x.User!.IsActive)
                .Select(x=>new{x.SessionId,x.UserAgent}).Take(100).ToListAsync(ct);
            return Content(active.Select(x=>new{id=x.SessionId,device=SessionDevice.Describe(x.UserAgent),ready=ready.Contains(x.SessionId!.Value)}));
        }
        if(tool.GetString() is "device.status" or "device.cancel")
        {
            if(args.EnumerateObject().Count()!=1||!args.TryGetProperty("requestId",out var requestId)||requestId.ValueKind!=JsonValueKind.String||!requestId.TryGetGuid(out var rid))return Error(-32602,"Invalid request ID");
            var value=tool.GetString()=="device.cancel"?queue.Cancel(owner,source,rid,now):queue.List(owner,source,now).FirstOrDefault(x=>x.Id==rid);
            return value is null?Error(-32602,"Request unavailable"):Content(new{value.Id,value.State,value.Expires,value.Action});
        }
        if(tool.GetString()=="avatar.handoff")
        {
            if(args.EnumerateObject().Count()!=4||!args.TryGetProperty("targetId",out var dest)||dest.ValueKind!=JsonValueKind.String||!dest.TryGetGuid(out var destination)||
                !args.TryGetProperty("motion",out var motion)||motion.ValueKind!=JsonValueKind.String||
                !args.TryGetProperty("mood",out var mood)||mood.ValueKind!=JsonValueKind.String||
                !args.TryGetProperty("phaseSeconds",out var phase)||phase.ValueKind!=JsonValueKind.Number||!phase.TryGetDouble(out var seconds))return Error(-32602,"Invalid avatar state");
            var snapshot=new DeviceActionQueue.AvatarSnapshot(motion.GetString()!,mood.GetString()!,seconds);
            if(!snapshot.Valid()||destination==source||!await Active(owner,destination,ct))return Error(-32602,"Avatar or target unavailable");
            var offered=queue.Handoff(owner,source,destination,snapshot,DateTimeOffset.UtcNow);
            return offered is null?Error(-32602,"Target unavailable or queue full"):Content(new{offered.Id,offered.State,offered.Expires});
        }
        if(tool.GetString()!="device.request"||!args.TryGetProperty("targetId",out var target)||target.ValueKind!=JsonValueKind.String||!target.TryGetGuid(out var targetId)||
            !args.TryGetProperty("action",out var action)||action.ValueKind!=JsonValueKind.String||args.EnumerateObject().Count()!=2)return Error(-32602,"Invalid tool arguments");
        if(targetId==source||!queue.Ready(owner,now).Contains(targetId)||!await Active(owner,targetId,ct))return Error(-32602,"Target not receiving requests");
        var item=queue.Request(owner,source,targetId,action.GetString()!,DateTimeOffset.UtcNow);
        return item is null?Error(-32602,"Action unavailable or queue full"):Result(new{content=new[]{new{type="text",text=JsonSerializer.Serialize(new{item.Id,item.State,item.Expires},new JsonSerializerOptions(JsonSerializerDefaults.Web))}},isError=false});
    }
}
