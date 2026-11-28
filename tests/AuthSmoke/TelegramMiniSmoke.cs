using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class TelegramMiniSmoke
{
    public static async Task Run()
    {
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE telegram_mini_sessions");
        await AccountSecuritySchema.UpgradeAsync(db,default);await AccountSecuritySchema.UpgradeAsync(db,default);
        var user=new User{Login="mini",PasswordHash="fixture"};
        db.Users.Add(user);await db.SaveChangesAsync();
        var realm=new MessengerRealm("telegram","123");var token="123:"+new string('t',32);
        var key=new ExternalIdentityKey("telegram","bot:123","42");
        await new ExternalIdentityStore(db).RecordVerifiedLink(user.Id,key);
        var now=DateTimeOffset.UtcNow.AddSeconds(2);
        string Signed(string userJson,long date,string query)
        {
            var fields=new SortedDictionary<string,string>(StringComparer.Ordinal)
            {["auth_date"]=date.ToString(System.Globalization.CultureInfo.InvariantCulture),["query_id"]=query,["user"]=userJson};
            var secret=HMACSHA256.HashData(Encoding.UTF8.GetBytes("WebAppData"),Encoding.UTF8.GetBytes(token));
            var hash=Convert.ToHexString(HMACSHA256.HashData(secret,Encoding.UTF8.GetBytes(string.Join("\n",fields.Select(x=>x.Key+"="+x.Value))))).ToLowerInvariant();
            return string.Join("&",fields.Select(x=>x.Key+"="+Uri.EscapeDataString(x.Value)))+"&hash="+hash;
        }
        void Check(bool ok,string label){if(!ok)throw new Exception("Telegram Mini: "+label);}
        var raw=Signed("""{"id":42,"first_name":"Fixture"}""",now.ToUnixTimeSeconds(),"q1");
        var proof=TelegramMiniProof.Verify(realm,token,raw,now)!;
        Check(proof is not null&&proof.Subject=="42","proof");
        Check(TelegramMiniProof.Verify(realm,token,raw+"&user=x",now) is null,"duplicate field");
        Check(TelegramMiniProof.Verify(realm,token,raw.Replace("42","43"),now) is null,"tamper");
        Check(TelegramMiniProof.Verify(realm,token,raw,now.AddMinutes(6)) is null,"expired");
        Check(TelegramMiniProof.Verify(realm,"124:"+new string('t',32),raw,now) is null,"wrong bot");
        Check(TelegramMiniProof.Verify(realm,token,Signed("""{"id":42,"id":43}""",now.ToUnixTimeSeconds(),"q"),now) is null,"duplicate user field");
        var service=new TelegramMiniSessions(db,TimeProvider.System);
        var session=(await service.Exchange(proof!))!;
        Check(session.Token.Length==43&&session.ExpiresInSeconds==600,"restricted token");
        Check(await service.Exchange(proof!) is null,"replay");
        var reordered=string.Join("&",raw.Split('&').Reverse());
        Check(await service.Exchange(TelegramMiniProof.Verify(realm,token,reordered,now)!) is null,"canonical replay");
        Check((await service.Resolve(realm,session.Token))?.UserId==user.Id,"resolve");
        Check(await service.Resolve(new("telegram","124"),session.Token) is null,"realm");
        var unknown=TelegramMiniProof.Verify(realm,token,Signed("""{"id":99}""",now.ToUnixTimeSeconds(),"q2"),now)!;
        Check(await service.Exchange(unknown) is null,"unlinked");
        await service.Revoke(realm,session.Token);
        Check(await service.Resolve(realm,session.Token) is null&&await service.Exchange(proof!) is null,"logout keeps receipt");
        var other=TelegramMiniProof.Verify(realm,token,Signed("""{"id":42}""",now.ToUnixTimeSeconds(),"q3"),now)!;
        var session2=(await service.Exchange(other))!;
        await db.ExternalIdentities.Where(i=>i.Provider==key.Provider&&i.Realm==key.Realm&&i.Subject==key.Subject).ExecuteDeleteAsync();
        db.ChangeTracker.Clear();
        Check(await service.Resolve(realm,session2.Token) is null,"unlink");
        await new ExternalIdentityStore(db).RecordVerifiedLink(user.Id,key);
        Check(await service.Resolve(realm,session2.Token) is null,"relink does not revive");
        Console.WriteLine("Telegram Mini App HMAC, one-use exchange, scope and revocation checks passed");
    }
}
