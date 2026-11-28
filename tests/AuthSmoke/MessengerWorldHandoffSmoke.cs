using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;
using Models.Autorization.Services;

internal static class MessengerWorldHandoffSmoke
{
    private sealed class Clock(DateTimeOffset start):TimeProvider
    {
        public DateTimeOffset Current=start;
        public override DateTimeOffset GetUtcNow()=>Current;
    }
    private sealed class Hasher:IPasswordHasher
    {
        public string Hash(string password)=>password;
        public bool Verify(string password,string hash)=>password==hash;
    }
    public static async Task Run()
    {
        var opt=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(opt);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE messenger_world_commands");
        await AccountSecuritySchema.UpgradeAsync(db,default);await AccountSecuritySchema.UpgradeAsync(db,default);
        var user=new User{Login="handoff",PasswordHash="fixture"};
        db.Users.Add(user);await db.SaveChangesAsync();
        var realm=new MessengerRealm("telegram","123");var key=new ExternalIdentityKey("telegram","bot:123","42");
        await new ExternalIdentityStore(db).RecordVerifiedLink(user.Id,key);
        var clock=new Clock(DateTimeOffset.UtcNow.AddSeconds(2));
        var identities=new MessengerIdentityService(db,clock,new Hasher());
        var ingress=new MessengerIngress(db,identities,clock);var queue=new MessengerWorldHandoff(db,clock);
        var secret=new string('x',32);long id=0;
        PrivateMessengerEvent Event(string text,long sender=42)=>MessengerWebhookProof.Telegram(realm,secret,secret,
            JsonSerializer.SerializeToElement(new{update_id=++id,message=new{date=clock.Current.ToUnixTimeSeconds(),text,
                from=new{id=sender,is_bot=false},chat=new{id=sender,type="private"}}}),clock.Current)!;
        void Check(bool ok,string label){if(!ok)throw new Exception("Messenger world handoff: "+label);}
        Check(await ingress.Process(realm,Event("/nearby"))==MessengerIngressStatus.Ignored,"disabled");
        Check(await ingress.Process(realm,Event("/nearby",99),queueWorld:true)==MessengerIngressStatus.WorldUnavailable,"unlinked");
        Check(await ingress.Process(realm,Event("/link fake"),queueWorld:true)==MessengerIngressStatus.LinkUnavailable,"link not stored");
        var e=Event("/talk 0123456789abcdef hello");
        Check(await ingress.Process(realm,e,queueWorld:true)==MessengerIngressStatus.WorldQueued,"queued");
        Check(await ingress.Process(realm,e,queueWorld:true)==MessengerIngressStatus.Duplicate,"receipt dedup");
        Check(await db.MessengerWorldCommands.CountAsync()==1,"one durable command");
        var worker=Guid.NewGuid();var other=Guid.NewGuid();
        var first=(await queue.Claim(worker))!;
        Check(first.UserId==user.Id&&first.Command==e.Text,"bound lease");
        Check(await queue.Claim(other) is null&&!await queue.Validate(other,first.Id,first.LeaseId),"exclusive lease");
        clock.Current=clock.Current.AddSeconds(31);
        Check(!await queue.Validate(worker,first.Id,first.LeaseId),"expiry");
        var second=(await queue.Claim(worker))!;
        Check(second.Id==first.Id&&second.LeaseId!=first.LeaseId,"stable command, new lease");
        Check(!await queue.Complete(worker,first.Id,first.LeaseId,true),"old ack denied");
        Check(await queue.Complete(worker,second.Id,second.LeaseId,true),"completion");
        Check(!await queue.Complete(worker,second.Id,second.LeaseId,true),"ack replay");
        Check(await db.MessengerWorldCommands.AsNoTracking().AnyAsync(x=>x.Id==second.Id&&x.Command==""&&x.Status==MessengerCommandState.Completed),"redacted");
        Check(await ingress.Process(realm,Event("/friends"),queueWorld:true)==MessengerIngressStatus.WorldQueued,"second command");
        var revoke=(await queue.Claim(worker))!;
        await db.ExternalIdentities.Where(i=>i.Provider==key.Provider&&i.Realm==key.Realm&&i.Subject==key.Subject).ExecuteDeleteAsync();
        Check(!await queue.Validate(worker,revoke.Id,revoke.LeaseId),"revoked link");
        await new ExternalIdentityStore(db).RecordVerifiedLink(user.Id,key);
        Check(!await queue.Validate(worker,revoke.Id,revoke.LeaseId),"relinked identity");
        await queue.Cleanup();
        Check(await db.MessengerWorldCommands.AsNoTracking().AnyAsync(x=>x.Id==revoke.Id&&x.Status==MessengerCommandState.Rejected&&x.Command==""),"revoked payload erased");
        Console.WriteLine("Messenger world handoff receipt, lease, replay and revocation checks passed");
    }
}
