using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

public enum ChatRpControl { GroupEnable,GroupDisable,MemberOn,MemberOff }
public sealed record ChatRpControlEvent(MessengerRealm Realm,string Peer,string SenderId,DateTimeOffset Occurred,ChatRpControl Control);

/// <summary>Preferences narrow, never expand, operator-approved configuration. Only a verified
/// group command and current provider role may reach Apply. No world or account state changes.</summary>
public sealed class ChatRpPreferences(AuthDbContext db,TimeProvider clock)
{
    public async Task<ChatRpGroupPolicy> Effective(ChatRpGroupPolicy baseline,CancellationToken ct=default)
    {
        var rows=await db.ChatRpPreferences.AsNoTracking().Where(x=>x.Provider==baseline.Realm.Provider&&
            x.BotId==baseline.Realm.BotId&&x.Peer==baseline.Peer&&!x.Enabled).Select(x=>x.Subject).ToListAsync(ct);
        var excluded=rows.ToHashSet(StringComparer.Ordinal);
        return baseline with {Enabled=baseline.Enabled&&!excluded.Contains("0"),
            Participants=baseline.Participants.Where(x=>!excluded.Contains(x)).ToHashSet(StringComparer.Ordinal)};
    }

    public async Task<bool> Apply(ChatRpControlEvent e,ChatRpGroupPolicy baseline,ChatRpMemberRole role,CancellationToken ct=default)
    {
        if(!baseline.Enabled||baseline.Realm!=e.Realm||baseline.Peer!=e.Peer||
            !ChatRpDestination.Valid(e.Realm,e.Peer)||!TelegramIdentityProof.PositiveId(e.SenderId)||
            !MessengerWebhookProof.Fresh(e.Occurred,clock.GetUtcNow())||
            e.Control is not (ChatRpControl.GroupEnable or ChatRpControl.GroupDisable or ChatRpControl.MemberOn or ChatRpControl.MemberOff))return false;
        var group=e.Control is ChatRpControl.GroupEnable or ChatRpControl.GroupDisable;
        if(group&&role!=ChatRpMemberRole.Admin)return false;
        if(!group&&(!baseline.Participants.Contains(e.SenderId)||
            e.Control==ChatRpControl.MemberOn&&role is not (ChatRpMemberRole.Member or ChatRpMemberRole.Admin)))return false;
        var subject=group?"0":e.SenderId;
        var enabled=e.Control is ChatRpControl.GroupEnable or ChatRpControl.MemberOn;
        var when=e.Occurred.UtcDateTime;
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Equal timestamps and stale/replayed provider commands cannot undo a newer decision.
        var changed=await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO chat_rp_preferences ("Provider","BotId","Peer","Subject","Enabled","ChangedUtc")
            VALUES ({e.Realm.Provider},{e.Realm.BotId},{e.Peer},{subject},{enabled},{when})
            ON CONFLICT ("Provider","BotId","Peer","Subject") DO UPDATE
                SET "Enabled"=excluded."Enabled","ChangedUtc"=excluded."ChangedUtc"
                WHERE chat_rp_preferences."ChangedUtc"<excluded."ChangedUtc"
            """,ct);
        if(changed!=1)return false;
        if(!enabled)
        {
            await db.ChatRpConsents.Where(x=>x.Provider==e.Realm.Provider&&x.BotId==e.Realm.BotId&&
                x.Peer==e.Peer&&!x.Consumed&&(group||x.ActorId==subject||x.TargetId==subject))
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Consumed,true).SetProperty(x=>x.ActorId,"")
                    .SetProperty(x=>x.TargetId,"").SetProperty(x=>x.ActorName,"").SetProperty(x=>x.TargetName,""),ct);
            await db.ChatRpDeliveries.Where(x=>x.Provider==e.Realm.Provider&&x.BotId==e.Realm.BotId&&
                x.Peer==e.Peer&&x.Status==ChatRpDeliveryState.Pending&&(group||x.ActorId==subject||x.TargetId==subject))
                .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,ChatRpDeliveryState.Rejected)
                    .SetProperty(x=>x.ActorId,"").SetProperty(x=>x.TargetId,"").SetProperty(x=>x.Caption,""),ct);
        }
        await tx.CommitAsync(ct);return true;
    }
}
