using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

[ApiController,SkipPermission,Route("api/auth/mobile-agent"),EnableRateLimiting("auth-sensitive")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MobileAgentController(AuthDbContext db,MobileAgentRequests queue):BaseApiController
{
    private Guid? Sid=>Guid.TryParse(User.FindFirst("mystic_session")?.Value,out var id)?id:null;
    private bool Account=>CurrentUserId.HasValue&&Sid.HasValue&&!User.HasClaim("token_use","device");
    private Task<bool> Active(Guid owner,Guid session,CancellationToken ct)=>db.RefreshTokens.AsNoTracking().AnyAsync(x=>x.UserId==owner&&x.SessionId==session&&x.ExpiresAt>DateTime.UtcNow&&x.User!.IsActive,ct);
    private async Task<bool> AccountActive(CancellationToken ct)=>Account&&await Active(CurrentUserId!.Value,Sid!.Value,ct);

    [Authorize,HttpPost("ticket"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Ticket(CancellationToken ct)
    {
        if(!await AccountActive(ct))return Unauthorized();
        var code=queue.Issue(CurrentUserId!.Value,Sid!.Value,DateTimeOffset.UtcNow);
        return code is null?StatusCode(429):Ok(new{code,expiresIn=120});
    }
    public sealed record EnrollInput(string Code,string Name,string Os);
    [AllowAnonymous,HttpPost("enroll"),RequestSizeLimit(2048)]
    public async Task<IActionResult> Enroll(EnrollInput input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.Name)||input.Name.Length>100||input.Name.Any(char.IsControl)||string.IsNullOrWhiteSpace(input.Os)||input.Os.Length>40||input.Os.Any(char.IsControl))return BadRequest();
        var ticket=queue.Consume(input.Code,DateTimeOffset.UtcNow);
        if(ticket is null||!await Active(ticket.Owner,ticket.Session,ct))return Unauthorized();
        if(await db.AccountMobileAgents.CountAsync(x=>x.UserId==ticket.Owner,ct)>=40)return Conflict(new{error="Device registration limit"});
        var secret=MobileAgentRequests.Secret();
        var agent=new AccountMobileAgent{Id=Guid.NewGuid(),UserId=ticket.Owner,SecretHash=MobileAgentRequests.Hash(secret),Name=input.Name.Trim(),Os=input.Os.Trim(),CreatedUtc=DateTime.UtcNow};
        db.AccountMobileAgents.Add(agent);await db.SaveChangesAsync(ct);
        return Ok(new{id=agent.Id,secret});
    }
    private async Task<AccountMobileAgent?> Agent(Guid id,string? secret,CancellationToken ct)
    {
        if(id==Guid.Empty||!MobileAgentRequests.ValidSecret(secret))return null;
        var row=await db.AccountMobileAgents.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.RevokedUtc==null&&db.Users.Any(u=>u.Id==x.UserId&&u.IsActive),ct);
        return row is not null&&MobileAgentRequests.Match(row.SecretHash,secret)?row:null;
    }
    private string? AgentSecret()=>Request.Headers.TryGetValue("X-Mystic-Agent-Key",out var value)&&value.Count==1?value[0]:null;
    public sealed record PollInput(Guid Id);
    [AllowAnonymous,HttpPost("poll"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Poll(PollInput input,CancellationToken ct)
    {
        var agent=await Agent(input.Id,AgentSecret(),ct);if(agent is null)return Unauthorized();
        var updated=await db.AccountMobileAgents.Where(x=>x.Id==agent.Id&&x.RevokedUtc==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LastSeenUtc,DateTime.UtcNow),ct);
        if(updated!=1)return Unauthorized();
        var requests=queue.ForTarget(agent.UserId,agent.Id,DateTimeOffset.UtcNow);
        return Ok(requests.Select(x=>new{x.Id,x.Action,x.Expires}));
    }
    public sealed record Decision(Guid Id,Guid RequestId,bool Approve);
    [AllowAnonymous,HttpPost("decide"),RequestSizeLimit(2048)]
    public async Task<IActionResult> Decide(Decision input,CancellationToken ct)
    {
        var agent=await Agent(input.Id,AgentSecret(),ct);if(agent is null)return Unauthorized();
        var now=DateTimeOffset.UtcNow;
        var item=queue.ForTarget(agent.UserId,agent.Id,now).FirstOrDefault(x=>x.Id==input.RequestId);
        if(item is null)return Conflict();
        if(input.Approve&&!await Active(agent.UserId,item.Source,ct))return Conflict();
        var result=queue.Decide(agent.UserId,agent.Id,input.RequestId,input.Approve,DateTimeOffset.UtcNow);
        return result is null?Conflict():Ok(new{result.Id,result.Action,result.State});
    }
    public sealed record Completion(Guid Id,Guid RequestId,bool Success,string? Result);
    [AllowAnonymous,HttpPost("complete"),RequestSizeLimit(2048)]
    public async Task<IActionResult> Complete(Completion input,CancellationToken ct)
    {
        var agent=await Agent(input.Id,AgentSecret(),ct);if(agent is null)return Unauthorized();
        if(input.Result?.Length>8)return BadRequest();
        var item=queue.Complete(agent.UserId,agent.Id,input.RequestId,input.Success,input.Result,DateTimeOffset.UtcNow);
        return item is null?Conflict():Ok(new{item.Id,item.State});
    }

    [Authorize,HttpGet("devices")]
    public async Task<IActionResult> Devices(CancellationToken ct)
    {
        if(!await AccountActive(ct))return Unauthorized();
        var owner=CurrentUserId!.Value;var now=DateTime.UtcNow;
        var agents=await db.AccountMobileAgents.AsNoTracking().Where(x=>x.UserId==owner).OrderByDescending(x=>x.CreatedUtc).Take(40).Select(x=>new{x.Id,x.Name,x.Os,x.CreatedUtc,x.LastSeenUtc,x.RevokedUtc}).ToListAsync(ct);
        return Ok(agents.Select(x=>new{x.Id,x.Name,x.Os,x.CreatedUtc,x.LastSeenUtc,x.RevokedUtc,ready=x.RevokedUtc==null&&x.LastSeenUtc>now.AddSeconds(-35)}));
    }
    [Authorize,HttpPost("revoke"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Revoke(PollInput input,CancellationToken ct)
    {
        if(!await AccountActive(ct))return Unauthorized();
        var owner=CurrentUserId!.Value;
        var count=await db.AccountMobileAgents.Where(x=>x.UserId==owner&&x.Id==input.Id&&x.RevokedUtc==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.RevokedUtc,DateTime.UtcNow),ct);
        if(count!=1)return NotFound();queue.Revoke(owner,input.Id);return NoContent();
    }
    [Authorize,HttpPost("mcp"),RequestSizeLimit(4096)]
    public async Task<IActionResult> Mcp(JsonElement q,CancellationToken ct)
    {
        if(!await AccountActive(ct))return Unauthorized();
        if(q.ValueKind!=JsonValueKind.Object||!q.TryGetProperty("jsonrpc",out var v)||v.ValueKind!=JsonValueKind.String||v.GetString()!="2.0"||!q.TryGetProperty("method",out var m)||m.ValueKind!=JsonValueKind.String)return BadRequest();
        var method=m.GetString();if(!q.TryGetProperty("id",out var id))return method=="notifications/initialized"?Accepted():BadRequest();
        if(id.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))return BadRequest();
        IActionResult Result(object value)=>Ok(new{jsonrpc="2.0",id,result=value});
        IActionResult Error(int code,string message)=>Ok(new{jsonrpc="2.0",id,error=new{code,message}});
        IActionResult Content(object value)=>Result(new{content=new[]{new{type="text",text=JsonSerializer.Serialize(value,new JsonSerializerOptions(JsonSerializerDefaults.Web))}},isError=false});
        object Schema(Dictionary<string,object> properties,string[] required)=>new{type="object",properties,required,additionalProperties=false};
        if(method=="initialize")return Result(new{protocolVersion="2024-11-05",capabilities=new{tools=new{}},serverInfo=new{name="MysticEgo Mobile Agent",version="0.1"}});
        if(method=="ping")return Result(new{});
        var uuid=new{type="string",format="uuid"};
        if(method=="tools/list")return Result(new{tools=new object[]{
            new{name="mobile.list",description="List registered, non-revoked Android agents of this account. Readiness is a recent heartbeat, not proof of connectivity.",inputSchema=Schema(new(),[])},
            new{name="mobile.request",description="Ask a registered Android agent for a locally confirmed fixed action. No arbitrary arguments or OS commands.",inputSchema=Schema(new(){["targetId"]=uuid,["action"]=new{type="string",@enum=MobileAgentRequests.Actions}},["targetId","action"])},
            new{name="mobile.status",description="List unexpired requests from this account session with device-reported results.",inputSchema=Schema(new(),[])}
        }});
        if(method!="tools/call")return Error(-32601,"Method not found");
        if(!q.TryGetProperty("params",out var p)||p.ValueKind!=JsonValueKind.Object||!p.TryGetProperty("name",out var n)||n.ValueKind!=JsonValueKind.String||!p.TryGetProperty("arguments",out var a)||a.ValueKind!=JsonValueKind.Object)return Error(-32602,"Invalid arguments");
        var owner=CurrentUserId!.Value;var sid=Sid!.Value;var now=DateTimeOffset.UtcNow;
        if(n.GetString()=="mobile.list"&&a.EnumerateObject().Count()==0)
        {
            var agents=await db.AccountMobileAgents.AsNoTracking().Where(x=>x.UserId==owner&&x.RevokedUtc==null).Take(40).Select(x=>new{x.Id,x.Name,x.Os,x.LastSeenUtc}).ToListAsync(ct);
            return Content(agents.Select(x=>new{x.Id,x.Name,x.Os,x.LastSeenUtc,ready=x.LastSeenUtc>now.UtcDateTime.AddSeconds(-35)}));
        }
        if(n.GetString()=="mobile.status"&&a.EnumerateObject().Count()==0)return Content(queue.ForSource(owner,sid,now).Select(x=>new{x.Id,x.Target,x.Action,x.State,x.Result,x.Expires}));
        if(n.GetString()!="mobile.request"||a.EnumerateObject().Count()!=2||!a.TryGetProperty("targetId",out var t)||t.ValueKind!=JsonValueKind.String||!t.TryGetGuid(out var target)||!a.TryGetProperty("action",out var action)||action.ValueKind!=JsonValueKind.String)return Error(-32602,"Invalid arguments");
        if(!await db.AccountMobileAgents.AsNoTracking().AnyAsync(x=>x.Id==target&&x.UserId==owner&&x.RevokedUtc==null&&x.LastSeenUtc>now.UtcDateTime.AddSeconds(-35),ct))return Error(-32602,"Agent unavailable");
        var item=queue.Request(owner,sid,target,action.GetString()!,now);
        return item is null?Error(-32602,"Action unavailable or queue full"):Content(new{item.Id,item.State,item.Expires});
    }
}
