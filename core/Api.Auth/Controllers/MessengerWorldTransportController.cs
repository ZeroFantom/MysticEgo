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
[ApiController, Authorize, SkipPermission, Models.Commands.SkipTool, Route("api/auth/messenger/internal/world")]
[EnableRateLimiting("messenger-webhook")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MessengerWorldTransportController(AuthDbContext db, MessengerWorldHandoff handoff,
    IConfiguration configuration) : ControllerBase
{
    public sealed record LeaseRequest(Guid Id,Guid LeaseId);
    public sealed record CompleteRequest(Guid Id,Guid LeaseId,bool Accepted);

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

    [HttpPost("claim"), RequestSizeLimit(1024)]
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

    [HttpPost("validate"), RequestSizeLimit(2048)]
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

    [HttpPost("complete"), RequestSizeLimit(2048)]
    public async Task<IActionResult> Complete(CompleteRequest input,CancellationToken ct)
    {
        var worker=await Worker(ct);
        if(worker is null)return StatusCode(403);
        return await handoff.Complete(worker.Value,input.Id,input.LeaseId,input.Accepted,ct)?NoContent():Conflict();
    }
}
