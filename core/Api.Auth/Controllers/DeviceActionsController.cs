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
        return Ok(queue.List(CurrentUserId.Value,Sid.Value,DateTimeOffset.UtcNow).Select(x=>new{x.Id,x.Source,x.Target,x.Action,x.Expires,x.State,incoming=x.Target==Sid.Value}));
    }

    public sealed record Decision(Guid Id,bool Approve);
    [HttpPost("decide"),RequestSizeLimit(4096)]
    public async Task<IActionResult> Decide(Decision input,CancellationToken ct)
    {
        if(!Account||!await Active(CurrentUserId!.Value,Sid!.Value,ct))return Unauthorized();
        var owner=CurrentUserId.Value;var session=Sid.Value;
        var pending=queue.List(owner,session,DateTimeOffset.UtcNow).FirstOrDefault(x=>x.Id==input.Id&&x.Target==session&&x.State=="pending");
        if(pending is null)return NotFound();
        if(input.Approve&&!await Active(owner,pending.Source,ct))return Conflict();
        var result=queue.Decide(owner,session,input.Id,input.Approve,DateTimeOffset.UtcNow);
        return result is null?Conflict():Ok(new{result.Id,result.Action,result.State});
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
        if(name=="tools/list")return Result(new{tools=new[]{new{name="device.request",description="Request a visible navigation action in another active session of this account. Requires approval on that session.",inputSchema=new{type="object",properties=new{targetId=new{type="string",format="uuid"},action=new{type="string",@enum=DeviceActionQueue.Allowed}},required=new[]{"targetId","action"},additionalProperties=false}}}});
        if(name!="tools/call")return Error(-32601,"Method not found");
        if(!request.TryGetProperty("params",out var p)||p.ValueKind!=JsonValueKind.Object||!p.TryGetProperty("name",out var tool)||tool.ValueKind!=JsonValueKind.String||tool.GetString()!="device.request"||
            !p.TryGetProperty("arguments",out var args)||args.ValueKind!=JsonValueKind.Object||!args.TryGetProperty("targetId",out var target)||target.ValueKind!=JsonValueKind.String||!target.TryGetGuid(out var targetId)||
            !args.TryGetProperty("action",out var action)||action.ValueKind!=JsonValueKind.String||args.EnumerateObject().Count()!=2)return Error(-32602,"Invalid tool arguments");
        var owner=CurrentUserId.Value;var source=Sid.Value;
        if(targetId==source||!await Active(owner,targetId,ct))return Error(-32602,"Target session unavailable");
        var item=queue.Request(owner,source,targetId,action.GetString()!,DateTimeOffset.UtcNow);
        return item is null?Error(-32602,"Action unavailable or queue full"):Result(new{content=new[]{new{type="text",text=JsonSerializer.Serialize(new{item.Id,item.State,item.Expires},new JsonSerializerOptions(JsonSerializerDefaults.Web))}},isError=false});
    }
}
