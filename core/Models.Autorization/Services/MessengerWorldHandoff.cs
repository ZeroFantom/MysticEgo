using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>Trusted internal queue, not a provider proof or game authorization.
/// Enqueue is called inside the ingress receipt transaction. Leases are at-least-once;
/// downstream must use stable Id for idempotency. No reply or world mutation here.</summary>
public sealed class MessengerWorldHandoff(AuthDbContext db, TimeProvider clock)
{
    private DateTime Now => clock.GetUtcNow().UtcDateTime;
    private const int MaxOutstanding=4096;
    private const int MaxAttempts=3;
    public sealed record Lease(Guid Id,Guid LeaseId,Guid UserId,string Provider,string BotId,string Subject,
        string EventId,string Command,DateTime OccurredUtc,DateTime LinkedUtc,DateTime ExpiresUtc,DateTime LeaseUntilUtc);

    public static bool IsWorldCommand(string text) =>
        text is "/nearby" or "/friends" || text.StartsWith("/talk ",StringComparison.Ordinal);

    internal async Task<bool> Enqueue(MessengerRealm realm,PrivateMessengerEvent message,CancellationToken ct)
    {
        if(db.Database.CurrentTransaction is null)throw new InvalidOperationException("Ingress transaction required.");
        var now=Now;
        if(!realm.Valid||!message.Sender.PrivateHumanChat||message.Sender.Provider!=realm.Provider||
            message.Sender.BotId!=realm.BotId||!MessengerWebhookProof.Fresh(message.Occurred,clock.GetUtcNow())||
            !IsWorldCommand(message.Text)||message.Text.Length>4000||
            message.Text.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t')))return false;
        var link=await (from i in db.ExternalIdentities.AsNoTracking()
                        join u in db.Users.AsNoTracking() on i.UserId equals u.Id
                        where u.IsActive&&i.Provider==realm.Provider&&i.Realm==realm.Realm&&i.Subject==message.Sender.UserId
                        select i).SingleOrDefaultAsync(ct);
        if(link is null||link.LinkedUtc>message.Occurred.UtcDateTime)return false;
        if(await db.MessengerWorldCommands.CountAsync(x=>x.ExpiresUtc>now&&
            (x.Status==MessengerCommandState.Pending||x.Status==MessengerCommandState.Leased),ct)>=MaxOutstanding)return false;
        var expiry=message.Occurred.UtcDateTime.AddMinutes(10);
        if(expiry<=now)return false;
        db.MessengerWorldCommands.Add(new MessengerWorldCommand
        {
            UserId=link.UserId,Provider=realm.Provider,BotId=realm.BotId,Subject=link.Subject,
            EventId=message.EventId,Command=message.Text,LinkedUtc=link.LinkedUtc,
            OccurredUtc=message.Occurred.UtcDateTime,ReceivedUtc=now,ExpiresUtc=expiry
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    // Link timestamp fences unlink/relink, even when the new owner is the same account.
    private IQueryable<MessengerWorldCommand> Live(DateTime now) =>
        db.MessengerWorldCommands.Where(c=>c.ExpiresUtc>now&&db.ExternalIdentities.Any(i=>
            i.Provider==c.Provider&&i.Realm=="bot:"+c.BotId&&i.Subject==c.Subject&&
            i.UserId==c.UserId&&i.LinkedUtc==c.LinkedUtc)&&db.Users.Any(u=>u.Id==c.UserId&&u.IsActive));

    public async Task<Lease?> Claim(Guid worker,CancellationToken ct=default)
    {
        if(worker==Guid.Empty)return null;
        var now=Now;
        var eligible=Live(now).Where(c=>c.Attempts<MaxAttempts&&
            (c.Status==MessengerCommandState.Pending||
             c.Status==MessengerCommandState.Leased&&c.LeaseUntilUtc<=now));
        var row=await eligible.AsNoTracking().OrderBy(c=>c.ReceivedUtc).ThenBy(c=>c.Id).FirstOrDefaultAsync(ct);
        if(row is null)return null;
        var lease=Guid.NewGuid();var until=now.AddSeconds(30);
        if(until>row.ExpiresUtc)until=row.ExpiresUtc;
        var changed=await eligible.Where(c=>c.Id==row.Id).ExecuteUpdateAsync(s=>s
            .SetProperty(c=>c.Status,MessengerCommandState.Leased)
            .SetProperty(c=>c.LeaseId,(Guid?)lease).SetProperty(c=>c.LeaseOwner,(Guid?)worker)
            .SetProperty(c=>c.LeaseUntilUtc,(DateTime?)until).SetProperty(c=>c.Attempts,c=>c.Attempts+1),ct);
        return changed!=1?null:new(row.Id,lease,row.UserId,row.Provider,row.BotId,row.Subject,row.EventId,
            row.Command,row.OccurredUtc,row.LinkedUtc,row.ExpiresUtc,until);
    }

    private IQueryable<MessengerWorldCommand> Held(Guid worker,Guid id,Guid lease,DateTime now) =>
        Live(now).Where(c=>c.Id==id&&c.Status==MessengerCommandState.Leased&&
            c.LeaseOwner==worker&&c.LeaseId==lease&&c.LeaseUntilUtc>now);

    /// <summary>Check immediately before consumption. This is not a cross-service transaction.</summary>
    public Task<bool> Validate(Guid worker,Guid id,Guid lease,CancellationToken ct=default) =>
        worker==Guid.Empty||id==Guid.Empty||lease==Guid.Empty?Task.FromResult(false):Held(worker,id,lease,Now).AnyAsync(ct);

    /// <summary>Terminal acknowledgement only; not a delivery receipt. Clear command text.</summary>
    public async Task<bool> Complete(Guid worker,Guid id,Guid lease,bool accepted,CancellationToken ct=default)
    {
        if(worker==Guid.Empty||id==Guid.Empty||lease==Guid.Empty)return false;
        return await Held(worker,id,lease,Now).ExecuteUpdateAsync(s=>s
            .SetProperty(c=>c.Status,accepted?MessengerCommandState.Completed:MessengerCommandState.Rejected)
            .SetProperty(c=>c.Command,"").SetProperty(c=>c.LeaseId,(Guid?)null)
            .SetProperty(c=>c.LeaseOwner,(Guid?)null).SetProperty(c=>c.LeaseUntilUtc,(DateTime?)null),ct)==1;
    }

    public sealed record ReplyDestination(Guid CommandId,Guid UserId,string Provider,string BotId,string Subject,DateTime LinkedUtc);
    // Claim the sole outbound attempt using the existing terminal transition. No retry after
    // an ambiguous provider result; a crash can lose a reply, never authorize a second attempt.
    public async Task<ReplyDestination?> TakeDiscoveryReply(Guid worker,Guid id,Guid lease,CancellationToken ct=default)
    {
        if(worker==Guid.Empty||id==Guid.Empty||lease==Guid.Empty)return null;
        var row=await Held(worker,id,lease,Now).AsNoTracking().SingleOrDefaultAsync(ct);
        if(row is null||!IsWorldCommand(row.Command))return null;
        if(!await Complete(worker,id,lease,true,ct))return null;
        return new(row.Id,row.UserId,row.Provider,row.BotId,row.Subject,row.LinkedUtc);
    }

    public Task<bool> ReplyStillLinked(ReplyDestination destination,CancellationToken ct=default)=>
        Live(Now).AnyAsync(c=>c.Id==destination.CommandId&&c.Status==MessengerCommandState.Completed&&
            c.UserId==destination.UserId&&c.Provider==destination.Provider&&c.BotId==destination.BotId&&
            c.Subject==destination.Subject&&c.LinkedUtc==destination.LinkedUtc,ct);

    /// <summary>Trusted maintenance; erase expired/revoked/exhausted command content.
    /// Retain terminal IDs until expiry+2 days to supplement ingress receipt dedup.</summary>
    public async Task<int> Cleanup(CancellationToken ct=default)
    {
        var now=Now;
        var valid=Live(now);
        await db.MessengerWorldCommands.Where(c=>(c.Status==MessengerCommandState.Pending||
            c.Status==MessengerCommandState.Leased)&&(!valid.Any(v=>v.Id==c.Id)||
                c.Attempts>=MaxAttempts&&c.LeaseUntilUtc<=now)).ExecuteUpdateAsync(s=>s
                    .SetProperty(c=>c.Status,MessengerCommandState.Rejected).SetProperty(c=>c.Command,"")
                    .SetProperty(c=>c.LeaseId,(Guid?)null).SetProperty(c=>c.LeaseOwner,(Guid?)null)
                    .SetProperty(c=>c.LeaseUntilUtc,(DateTime?)null),ct);
        var cutoff=now.AddDays(-2);
        return await db.MessengerWorldCommands.Where(c=>c.ExpiresUtc<cutoff).ExecuteDeleteAsync(ct);
    }
}
