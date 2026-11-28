using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>Deployment-owned per-group opt-in. Do not accept these flags from webhook JSON.
/// Admin authority and participant opt-in must be verified by operator before configuration.</summary>
public sealed record ChatRpGroupPolicy(MessengerRealm Realm,string Peer,bool Enabled,IReadOnlySet<string> Participants)
{
    public ChatRpCustomCatalog CustomActions { get; init; }=ChatRpCustomCatalog.Empty;
    public bool Allows(ChatRpEvent e)=>Enabled&&Realm==e.Realm&&Peer==e.Peer&&
        Participants.Contains(e.Actor.Id)&&Participants.Contains(e.Target.Id)&&
        (e.Custom is null||e.Custom.Action==e.Action&&CustomActions.Contains(e.Custom));
}

public static class ChatRpDestination
{
    public static bool Valid(MessengerRealm realm,string peer)
    {
        if(!realm.Valid||!long.TryParse(peer,NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var id)||
            id.ToString(CultureInfo.InvariantCulture)!=peer)return false;
        return realm.Provider=="telegram"?id<0:id>=2000000001;
    }
}

/// <summary>Group-only durable dispatch. Conditional claim before network I/O, no auto retry
/// after an ambiguous send. Terminal rows erase caption and participant identifiers.
/// No private account links, world messages, arbitrary text or consent-sensitive actions.</summary>
public sealed class ChatRpRuntime(AuthDbContext db,TimeProvider clock)
{
    public Task<Guid?> Queue(ChatRpEvent e,ChatRpGroupPolicy policy,CancellationToken ct=default) =>
        QueueCore(e,policy,false,ct);

    // Only ChatRpConsentService calls this inside the transaction consuming a target decision.
    internal Task<Guid?> QueueConsented(ChatRpEvent e,ChatRpGroupPolicy policy,CancellationToken ct) =>
        QueueCore(e,policy,true,ct);

    private async Task<Guid?> QueueCore(ChatRpEvent e,ChatRpGroupPolicy policy,bool consented,CancellationToken ct)
    {
        if(!policy.Allows(e)||!ChatRpDestination.Valid(e.Realm,e.Peer)||!MessengerWebhookProof.Fresh(e.Occurred,clock.GetUtcNow())||
            !TelegramIdentityProof.PositiveId(e.Actor.Id)||!TelegramIdentityProof.PositiveId(e.Target.Id)||
            e.Actor.Id==e.Target.Id||e.EventId is not {Length:>0 and <=128}||
            e.EventId.Any(c=>!(c is >= '!' and <= '~'))||
            ChatRpCatalog.Find(e.Action) is not { } action||action.RequiresConsent&&!consented)return null;
        var now=clock.GetUtcNow().UtcDateTime;
        // Advisory throttle under concurrency; deploy a strict provider/peer limiter as well.
        var cutoff=now.AddMinutes(-1);
        if(await db.ChatRpDeliveries.CountAsync(x=>x.Provider==e.Realm.Provider&&x.BotId==e.Realm.BotId&&
            x.Peer==e.Peer&&x.CreatedUtc>cutoff,ct)>=12)return null;
        var id=Guid.NewGuid();
        var caption=e.Custom?.Render(e.Actor,e.Target)??$"{ChatRpCatalog.Name(e.Actor.Name)} {action.Verb} «{ChatRpCatalog.Name(e.Target.Name)}».";
        var inserted=await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO chat_rp_deliveries ("Id","Provider","BotId","Peer","EventId","ActorId","TargetId","Caption","Category","Status","CreatedUtc","ExpiresUtc")
            VALUES ({id},{e.Realm.Provider},{e.Realm.BotId},{e.Peer},{e.EventId},{e.Actor.Id},{e.Target.Id},{caption},{action.Category},{(int)ChatRpDeliveryState.Pending},{now},{now.AddMinutes(2)})
            ON CONFLICT ("Provider","BotId","EventId") DO NOTHING
            """,ct);
        return inserted==1?id:null;
    }

    /// <summary>Consume exactly one local send attempt, checking live policy before claim.
    /// Caller must check current bot configuration and provider membership before network I/O.</summary>
    public async Task<ChatRpDelivery?> Claim(Guid id,ChatRpGroupPolicy policy,CancellationToken ct=default)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        var row=await db.ChatRpDeliveries.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct);
        if(row is null||row.Status!=ChatRpDeliveryState.Pending||row.ExpiresUtc<=now)return null;
        if(!policy.Enabled||row.Provider!=policy.Realm.Provider||row.BotId!=policy.Realm.BotId||
            row.Peer!=policy.Peer||!policy.Participants.Contains(row.ActorId)||!policy.Participants.Contains(row.TargetId))
        {
            await FinishPending(id,ct);return null;
        }
        var claimed=await db.ChatRpDeliveries.Where(x=>x.Id==id&&x.Status==ChatRpDeliveryState.Pending&&x.ExpiresUtc>now)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,ChatRpDeliveryState.Sending),ct);
        return claimed==1?row:null;
    }

    private Task<int> FinishPending(Guid id,CancellationToken ct)=>
        db.ChatRpDeliveries.Where(x=>x.Id==id&&x.Status==ChatRpDeliveryState.Pending)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,ChatRpDeliveryState.Rejected)
                .SetProperty(x=>x.Caption,"").SetProperty(x=>x.ActorId,"").SetProperty(x=>x.TargetId,""),ct);

    public async Task<bool> Finish(Guid id,ChatRpDeliveryState state,CancellationToken ct=default)
    {
        if(state is not (ChatRpDeliveryState.Sent or ChatRpDeliveryState.Uncertain or ChatRpDeliveryState.Rejected))return false;
        return await db.ChatRpDeliveries.Where(x=>x.Id==id&&x.Status==ChatRpDeliveryState.Sending)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,state).SetProperty(x=>x.Caption,"")
                .SetProperty(x=>x.ActorId,"").SetProperty(x=>x.TargetId,""),ct)==1;
    }

    public async Task<int> Cleanup(CancellationToken ct=default)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        await db.ChatRpDeliveries.Where(x=>x.ExpiresUtc<=now&&
            (x.Status==ChatRpDeliveryState.Pending||x.Status==ChatRpDeliveryState.Sending))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,x=>x.Status==ChatRpDeliveryState.Sending?
                ChatRpDeliveryState.Uncertain:ChatRpDeliveryState.Rejected)
                .SetProperty(x=>x.Caption,"").SetProperty(x=>x.ActorId,"").SetProperty(x=>x.TargetId,""),ct);
        return await db.ChatRpDeliveries.Where(x=>x.CreatedUtc<now.AddDays(-2)).ExecuteDeleteAsync(ct);
    }
}
