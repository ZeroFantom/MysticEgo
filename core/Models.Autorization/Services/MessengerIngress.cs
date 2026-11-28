using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;

namespace Models.Autorization.Services;

public enum MessengerIngressStatus { Ignored, Duplicate, LinkStaged, LinkUnavailable, Linked, Unlinked, Help, WorldQueued, WorldUnavailable }

/// <summary>
/// Authenticated private ingress only. Receipt and staging of a link candidate commit together.
/// No provider replies, world sends or external sessions are issued here. All unrecognized
/// text is discarded; an authoritative world adapter is required for communication.
/// </summary>
public sealed class MessengerIngress(AuthDbContext db, MessengerIdentityService identities, TimeProvider clock)
{
    public async Task<MessengerIngressStatus> Process(MessengerRealm realm, PrivateMessengerEvent message, CancellationToken ct=default, bool queueWorld=false)
    {
        if (!realm.Valid || message.Sender.Provider!=realm.Provider || message.Sender.BotId!=realm.BotId ||
            !message.Sender.PrivateHumanChat || !MessengerWebhookProof.Fresh(message.Occurred,clock.GetUtcNow()))
            return MessengerIngressStatus.Ignored;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Both supported databases implement ON CONFLICT; no race between lookup and insert.
        var claimed=await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO messenger_event_receipts ("Provider","BotId","EventId","ReceivedUtc")
            VALUES ({realm.Provider},{realm.BotId},{message.EventId},{clock.GetUtcNow().UtcDateTime})
            ON CONFLICT ("Provider","BotId","EventId") DO NOTHING
            """,ct);
        if(claimed!=1)return MessengerIngressStatus.Duplicate;
        var text=message.Text;
        var result=MessengerIngressStatus.Ignored;
        if(text.StartsWith("/link ",StringComparison.Ordinal)||text.StartsWith("/start ",StringComparison.Ordinal))
        {
            var code=text[(text.IndexOf(' ')+1)..];
            result=await identities.Observe(realm,message.Sender,code,ct)?MessengerIngressStatus.LinkStaged:MessengerIngressStatus.LinkUnavailable;
        }
        else if(text is "/start" or "/help")result=MessengerIngressStatus.Help;
        else if(text=="/status")
            result=await identities.Resolve(realm,message.Sender,ct) is null?MessengerIngressStatus.Unlinked:MessengerIngressStatus.Linked;
        else if(queueWorld&&MessengerWorldHandoff.IsWorldCommand(text))
            result=await new MessengerWorldHandoff(db,clock).Enqueue(realm,message,ct)
                ?MessengerIngressStatus.WorldQueued:MessengerIngressStatus.WorldUnavailable;
        await tx.CommitAsync(ct);
        return result;
    }

    // Trusted scheduled maintenance only. Retention exceeds accepted event freshness.
    public Task<int> PurgeExpired(CancellationToken ct=default)
    {
        var cutoff=clock.GetUtcNow().UtcDateTime.AddDays(-2);
        return db.MessengerEventReceipts.Where(r=>r.ReceivedUtc<cutoff).ExecuteDeleteAsync(ct);
    }
}
