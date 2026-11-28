using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Attributes;
using Models.Autorization.Data;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Restricted internal transport. Public clients and generic devices are denied.
/// Do not expose this prefix on the public reverse proxy.</summary>
[ApiController, Authorize, SkipPermission, Route("api/auth/messenger/internal/world")]
[EnableRateLimiting("messenger-webhook")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MessengerWorldTransportController(AuthDbContext db, MessengerWorldHandoff handoff,
    IConfiguration configuration,MessengerOutbound outbound) : ControllerBase
{
    public sealed record LeaseRequest(Guid Id,Guid LeaseId);
    public sealed record CompleteRequest(Guid Id,Guid LeaseId,bool Accepted);
    public sealed record DiscoveryContact(string Name,string Kind,string Reachability);
    public sealed record DiscoveryReplyRequest(Guid Id,Guid LeaseId,bool Available,
        DateTimeOffset? ValidUntilUtc,DiscoveryContact[]? Contacts);

    private async Task<Guid?> Worker(CancellationToken ct)
    {
        if(!configuration.GetValue<bool>("Messenger:WorldTransport:Enabled")||
            !Guid.TryParse(configuration["Messenger:WorldTransport:OrchestraIdentityId"],out var configured)||
            configured==Guid.Empty||User.Identity?.IsAuthenticated!=true||!User.HasClaim("token_use","device")||
            !User.HasClaim("identity_kind","orchestra")||!User.IsInRole("orchestra")||
            !Guid.TryParse(User.FindFirst("identity_id")?.Value,out var id)||id!=configured)
            return null;
        return await db.ManagedIdentities.AsNoTracking().AnyAsync(d=>d.Id==id&&d.Enabled&&d.Kind=="orchestra",ct)?id:null;
    }

    [HttpPost("claim"), Models.Commands.SkipTool, RequestSizeLimit(1024)]
    public async Task<IActionResult> Claim(CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        var worker=await Worker(ct);
        if(worker is null)return StatusCode(403);
        var lease=await handoff.Claim(worker.Value,ct);
        if(lease is null)return NoContent();
        var realm=MessengerConfiguration.Read(configuration,lease.Provider);
        if(realm is null||realm.Value.Realm.BotId!=lease.BotId)
        {
            await handoff.Complete(worker.Value,lease.Id,lease.LeaseId,false,ct);
            return NoContent();
        }
        return Ok(lease);
    }

    [HttpPost("validate"), Models.Commands.SkipTool, RequestSizeLimit(2048)]
    public async Task<IActionResult> Validate(LeaseRequest input,CancellationToken ct)
    {
        var worker=await Worker(ct);
        if(worker is null)return StatusCode(403);
        var row=await db.MessengerWorldCommands.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,ct);
        if(row is null)return Conflict();
        var realm=MessengerConfiguration.Read(configuration,row.Provider);
        if(realm is null||realm.Value.Realm.BotId!=row.BotId)return Conflict();
        return await handoff.Validate(worker.Value,input.Id,input.LeaseId,ct)?NoContent():Conflict();
    }

    [HttpPost("discovery-reply"),Models.Commands.SkipTool,RequestSizeLimit(16384)]
    public async Task<IActionResult> DiscoveryReply(DiscoveryReplyRequest input,CancellationToken ct)
    {
        if(!configuration.GetValue<bool>("Messenger:WorldTransport:DiscoveryReplyEnabled"))return NotFound();
        var worker=await Worker(ct);
        if(worker is null)return StatusCode(403);
        var row=await db.MessengerWorldCommands.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,ct);
        if(row is null||!MessengerWorldHandoff.IsWorldCommand(row.Command))return Conflict();
        var realm=new MessengerRealm(row.Provider,row.BotId);
        var token=MessengerConfiguration.ReplyToken(configuration,realm);
        if(string.IsNullOrWhiteSpace(token))return StatusCode(503);
        var now=DateTimeOffset.UtcNow;
        string text;
        if(row.Command.StartsWith("/talk ",StringComparison.Ordinal))
        {
            if(input.Available||input.Contacts is {Length:>0})return BadRequest();
            text="Духовные диалоги пока не подключены.";
        }
        else if(!input.Available)
        {
            if(input.Contacts is {Length:>0})return BadRequest();
            text="Свежие данные игрового мира недоступны.";
        }
        else
        {
            if(input.ValidUntilUtc is not { } until||until<=now||until>now.AddSeconds(15)||
                input.Contacts is not {Length:<=20} contacts||contacts.Any(c=>c is null||
                string.IsNullOrWhiteSpace(c.Name)||c.Name.Length>80||c.Name.Any(ch=>char.IsControl(ch)||
                    char.GetUnicodeCategory(ch)==System.Globalization.UnicodeCategory.Format)||
                c.Kind is not ("npc" or "player")||c.Reachability is not ("local" or "friend")||
                (row.Command=="/nearby"&&c.Reachability!="local")||
                (row.Command=="/friends"&&c.Kind!="player")))return BadRequest();
            var lines=contacts.Select(c=>c.Name+" — "+(c.Kind=="npc"?"NPC":"игрок")+
                (c.Reachability=="friend"?" · другая область":" · рядом"));
            text=contacts.Length==0?"Доступных собеседников нет.":(row.Command=="/nearby"?"Окружение:":"Друзья:")+"\n"+string.Join("\n",lines);
            text+="\nСписок только для просмотра.";
        }
        var destination=await handoff.TakeDiscoveryReply(worker.Value,input.Id,input.LeaseId,ct);
        if(destination is null)return Conflict();
        // Terminal state is not a provider delivery receipt. Recheck current link/config and expiry.
        if(await handoff.ReplyStillLinked(destination,ct)&&
            MessengerConfiguration.ReplyToken(configuration,realm) is {Length:>0} liveToken&&
            (!input.Available||input.ValidUntilUtc>DateTimeOffset.UtcNow))
            await outbound.DiscoveryReply(destination,text,liveToken,ct);
        return NoContent();
    }

    [HttpPost("complete"), Models.Commands.SkipTool, RequestSizeLimit(2048)]
    public async Task<IActionResult> Complete(CompleteRequest input,CancellationToken ct)
    {
        var worker=await Worker(ct);
        if(worker is null)return StatusCode(403);
        return await handoff.Complete(worker.Value,input.Id,input.LeaseId,input.Accepted,ct)?NoContent():Conflict();
    }
}
