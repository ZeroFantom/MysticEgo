using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>Opaque Mini App capabilities only. Never issue a general account JWT or refresh token.</summary>
public sealed class TelegramMiniSessions(AuthDbContext db,TimeProvider clock)
{
    public sealed record Session(string Token,int ExpiresInSeconds);
    public sealed record Principal(Guid UserId);
    private static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static bool ValidBearer(string? token)=>token is {Length:43}&&
        token.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');

    public async Task<Session?> Exchange(TelegramMiniProof.Verified verified,CancellationToken ct=default)
    {
        var now=clock.GetUtcNow();
        if(!TelegramMiniProof.Fresh(verified.Issued,now))return null;
        var realm="bot:"+verified.BotId;
        var link=await (from i in db.ExternalIdentities.AsNoTracking()
                        join u in db.Users.AsNoTracking() on i.UserId equals u.Id
                        where u.IsActive&&i.Provider=="telegram"&&i.Realm==realm&&i.Subject==verified.Subject
                        select i).SingleOrDefaultAsync(ct);
        if(link is null||link.LinkedUtc>verified.Issued.UtcDateTime)return null;
        var utc=now.UtcDateTime;
        if(await db.TelegramMiniSessions.CountAsync(x=>x.UserId==link.UserId&&!x.Revoked&&x.ExpiresUtc>utc,ct)>=5)return null;
        var token=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+','-').Replace('/','_');
        var hash=Hash(token);
        var inserted=await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO telegram_mini_sessions ("ProofKey","TokenHash","BotId","Subject","UserId","LinkedUtc","ExpiresUtc","RetainUntilUtc","Revoked")
            VALUES ({verified.ProofKey},{hash},{verified.BotId},{verified.Subject},{link.UserId},{link.LinkedUtc},{utc.AddMinutes(10)},{utc.AddDays(2)},{false})
            ON CONFLICT ("ProofKey") DO NOTHING
            """,ct);
        return inserted==1?new(token,600):null;
    }

    public async Task<Principal?> Resolve(MessengerRealm realm,string? token,CancellationToken ct=default)
    {
        if(!realm.Valid||realm.Provider!="telegram"||!ValidBearer(token))return null;
        var hash=Hash(token!);var now=clock.GetUtcNow().UtcDateTime;var ns="bot:"+realm.BotId;
        var account=await db.TelegramMiniSessions.AsNoTracking().Where(s=>s.BotId==realm.BotId&&s.TokenHash==hash&&
            !s.Revoked&&s.ExpiresUtc>now&&db.Users.Any(u=>u.Id==s.UserId&&u.IsActive)&&
            db.ExternalIdentities.Any(i=>i.Provider=="telegram"&&i.Realm==ns&&i.Subject==s.Subject&&
                i.UserId==s.UserId&&i.LinkedUtc==s.LinkedUtc)).Select(s=>(Guid?)s.UserId).SingleOrDefaultAsync(ct);
        return account is Guid id?new(id):null;
    }

    public async Task Revoke(MessengerRealm realm,string? token,CancellationToken ct=default)
    {
        if(!realm.Valid||realm.Provider!="telegram"||!ValidBearer(token))return;
        var hash=Hash(token!);
        await db.TelegramMiniSessions.Where(s=>s.BotId==realm.BotId&&s.TokenHash==hash)
            .ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Revoked,true),ct);
    }

    public Task<int> Cleanup(CancellationToken ct=default)
    {
        var now=clock.GetUtcNow().UtcDateTime;
        return db.TelegramMiniSessions.Where(x=>x.RetainUntilUtc<=now).ExecuteDeleteAsync(ct);
    }
}
