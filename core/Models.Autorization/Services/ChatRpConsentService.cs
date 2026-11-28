using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>Trusted group adapter decision, never accepted from an arbitrary public DTO.</summary>
public sealed record ChatRpDecision(MessengerRealm Realm,string Peer,string SenderId,Guid Id,bool Accept,string? CallbackId=null);

public sealed class ChatRpConsentService(AuthDbContext db,ChatRpRuntime runtime,TimeProvider clock)
{
    public sealed record Invitation(Guid Id,string Text);
    public sealed record Accepted(Guid DeliveryId,ChatRpEvent Event);

    public async Task<Invitation?> Propose(ChatRpEvent e,ChatRpGroupPolicy policy,CancellationToken ct=default)
    {
        if(!policy.Allows(e)||!ChatRpDestination.Valid(e.Realm,e.Peer)||
            !MessengerWebhookProof.Fresh(e.Occurred,clock.GetUtcNow())||
            !TelegramIdentityProof.PositiveId(e.Actor.Id)||!TelegramIdentityProof.PositiveId(e.Target.Id)||
            e.Actor.Id==e.Target.Id||e.EventId is not {Length:>0 and <=128}||
            e.EventId.Any(c=>!(c is >= '!' and <= '~'))||
            ChatRpCatalog.Find(e.Action) is not {RequiresConsent:true} action)return null;
        var now=clock.GetUtcNow().UtcDateTime;
        if(await db.ChatRpConsents.CountAsync(x=>x.Provider==e.Realm.Provider&&x.BotId==e.Realm.BotId&&
            x.Peer==e.Peer&&x.CreatedUtc>now.AddMinutes(-1),ct)>=6)return null;
        var id=Guid.NewGuid();
        var actor=ChatRpCatalog.Name(e.Actor.Name);var target=ChatRpCatalog.Name(e.Target.Name);
        var inserted=await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO chat_rp_consents ("Id","Provider","BotId","Peer","EventId","ActorId","TargetId","ActorName","TargetName","Action","CreatedUtc","ExpiresUtc","Consumed")
            VALUES ({id},{e.Realm.Provider},{e.Realm.BotId},{e.Peer},{e.EventId},{e.Actor.Id},{e.Target.Id},{actor},{target},{(int)e.Action},{now},{now.AddMinutes(2)},{false})
            ON CONFLICT ("Provider","BotId","EventId") DO NOTHING
            """,ct);
        return inserted==1?new(id,$"{actor} предлагает действие «{action.Command}» участнику «{target}». Только адресат может ответить в течение двух минут."):null;
    }

    public async Task<Accepted?> Decide(ChatRpDecision d,ChatRpGroupPolicy policy,CancellationToken ct=default)
    {
        if(!policy.Enabled||policy.Realm!=d.Realm||policy.Peer!=d.Peer||!policy.Participants.Contains(d.SenderId)||
            !ChatRpDestination.Valid(d.Realm,d.Peer)||d.Id==Guid.Empty)return null;
        var now=clock.GetUtcNow().UtcDateTime;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        var live=db.ChatRpConsents.Where(x=>x.Id==d.Id&&x.Provider==d.Realm.Provider&&x.BotId==d.Realm.BotId&&
            x.Peer==d.Peer&&x.TargetId==d.SenderId&&!x.Consumed&&x.ExpiresUtc>now);
        var row=await live.AsNoTracking().SingleOrDefaultAsync(ct);
        if(row is null||!policy.Participants.Contains(row.ActorId))return null;
        if(await live.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Consumed,true)
            .SetProperty(x=>x.ActorId,"").SetProperty(x=>x.TargetId,"")
            .SetProperty(x=>x.ActorName,"").SetProperty(x=>x.TargetName,""),ct)!=1)return null;
        Guid? delivery=null;
        var e=new ChatRpEvent(d.Realm,row.Peer,row.EventId,new DateTimeOffset(DateTime.SpecifyKind(now,DateTimeKind.Utc)),
            new(row.ActorId,row.ActorName),new(row.TargetId,row.TargetName),(ChatRpAction)row.Action);
        if(d.Accept)delivery=await runtime.QueueConsented(e,policy,ct);
        await tx.CommitAsync(ct);
        return delivery is Guid id?new(id,e):null;
    }

    public async Task<int> Cleanup(CancellationToken ct=default)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        await db.ChatRpConsents.Where(x=>!x.Consumed&&x.ExpiresUtc<=now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Consumed,true).SetProperty(x=>x.ActorId,"")
                .SetProperty(x=>x.TargetId,"").SetProperty(x=>x.ActorName,"").SetProperty(x=>x.TargetName,""),ct);
        return await db.ChatRpConsents.Where(x=>x.CreatedUtc<now.AddDays(-2)).ExecuteDeleteAsync(ct);
    }
}
