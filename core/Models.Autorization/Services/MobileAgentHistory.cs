using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>Durable request ledger; compare-and-set transitions never reissue an accepted action.</summary>
public sealed class MobileAgentHistory(AuthDbContext db)
{
    private static readonly SemaphoreSlim Admission=new(1,1);
    private IQueryable<MobileAgentAction> Owned(Guid owner)=>db.MobileAgentActions.Where(x=>x.UserId==owner);
    private IQueryable<MobileAgentAction> Live(Guid owner,Guid target,DateTime now)=>
        Owned(owner).Where(x=>x.Target==target&&x.ExpiresUtc>now&&
            db.AccountMobileAgents.Any(a=>a.Id==target&&a.UserId==owner&&a.RevokedUtc==null)&&db.Users.Any(u=>u.Id==owner&&u.IsActive));

    public async Task<MobileAgentAction?> Request(Guid owner,Guid source,Guid target,string action,DateTime now,CancellationToken ct)
    {
        if(owner==Guid.Empty||source==Guid.Empty||target==Guid.Empty||!MobileAgentRequests.Actions.Contains(action,StringComparer.Ordinal))return null;
        await Admission.WaitAsync(ct);
        try
        {
            await Cleanup(owner,now,ct);
            if(!await db.AccountMobileAgents.AnyAsync(x=>x.Id==target&&x.UserId==owner&&x.RevokedUtc==null&&x.LastSeenUtc>now.AddSeconds(-35),ct)||
                !await db.RefreshTokens.AnyAsync(x=>x.UserId==owner&&x.SessionId==source&&x.ExpiresAt>now&&x.User!.IsActive,ct))return null;
            if(await Owned(owner).CountAsync(x=>x.ExpiresUtc>now,ct)>=20||await Owned(owner).CountAsync(ct)>=2000)return null;
            var row=new MobileAgentAction{Id=Guid.NewGuid(),UserId=owner,Source=source,Target=target,Action=action,State="pending",CreatedUtc=now,UpdatedUtc=now,ExpiresUtc=now.AddMinutes(2)};
            db.MobileAgentActions.Add(row);await db.SaveChangesAsync(ct);return row;
        }
        finally{Admission.Release();}
    }
    // Thirty-day retention on owner activity; the operator still needs a periodic retention job for inactive accounts.
    public async Task Cleanup(Guid owner,DateTime now,CancellationToken ct)
    {await Owned(owner).Where(x=>x.CreatedUtc<now.AddDays(-30)).ExecuteDeleteAsync(ct);}
    public Task<List<MobileAgentAction>> Pending(Guid owner,Guid target,DateTime now,CancellationToken ct)=>
        Live(owner,target,now).AsNoTracking().Where(x=>x.State=="pending").OrderBy(x=>x.CreatedUtc).Take(20).ToListAsync(ct);
    public async Task<MobileAgentAction?> Decide(Guid owner,Guid target,Guid id,bool approve,DateTime now,CancellationToken ct)
    {
        var query=Live(owner,target,now).Where(x=>x.Id==id&&x.State=="pending");
        if(approve)query=query.Where(x=>db.RefreshTokens.Any(t=>t.UserId==owner&&t.SessionId==x.Source&&t.ExpiresAt>now));
        var changed=await query.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,approve?"accepted":"declined").SetProperty(x=>x.UpdatedUtc,now),ct);
        return changed==1?await Owned(owner).AsNoTracking().SingleAsync(x=>x.Id==id,ct):null;
    }
    public async Task<MobileAgentAction?> Complete(Guid owner,Guid target,Guid id,bool success,string? result,DateTime now,CancellationToken ct)
    {
        var query=Live(owner,target,now).Where(x=>x.Id==id&&x.State=="accepted");
        var action=await query.Select(x=>x.Action).SingleOrDefaultAsync(ct);
        if(action is null)return null;
        if(success&&(action=="battery-status"?!int.TryParse(result,out var battery)||battery<0||battery>100||result!=battery.ToString(System.Globalization.CultureInfo.InvariantCulture):result!="ok"))return null;
        var changed=await query.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,success?"reported-success":"reported-failure").SetProperty(x=>x.Result,success?result:null).SetProperty(x=>x.UpdatedUtc,now),ct);
        return changed==1?await Owned(owner).AsNoTracking().SingleAsync(x=>x.Id==id,ct):null;
    }
    public async Task Revoke(Guid owner,Guid target,DateTime now,CancellationToken ct)
    {
        await Owned(owner).Where(x=>x.Target==target&&x.ExpiresUtc>now&&(x.State=="pending"||x.State=="accepted"))
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.State,"revoked").SetProperty(x=>x.UpdatedUtc,now),ct);
    }
    public async Task<List<MobileAgentAction>> Read(Guid owner,Guid? source,DateTime now,CancellationToken ct)
    {
        await Cleanup(owner,now,ct);
        var query=Owned(owner).AsNoTracking();
        if(source.HasValue)query=query.Where(x=>x.Source==source.Value);
        return await query.OrderByDescending(x=>x.CreatedUtc).Take(50).ToListAsync(ct);
    }
    public static string DisplayState(MobileAgentAction item,DateTime now)=>
        item.ExpiresUtc<=now?item.State switch{"pending"=>"expired","accepted"=>"result-unknown",_=>item.State}:item.State;
}
