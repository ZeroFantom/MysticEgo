using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;
using Models.Autorization.Services;

internal static class MessengerIngressSmoke
{
    private sealed class Hasher:IPasswordHasher
    {
        public string Hash(string password)=>"fixture:"+password;
        public bool Verify(string password,string hash)=>Hash(password)==hash;
    }
    public static async Task Run()
    {
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE messenger_event_receipts");
        await AccountSecuritySchema.UpgradeAsync(db,default);
        await AccountSecuritySchema.UpgradeAsync(db,default);
        var h=new Hasher();var user=new User{Login="ingress",PasswordHash=h.Hash("password")};
        var session=Guid.NewGuid();
        db.Users.Add(user);db.RefreshTokens.Add(new RefreshToken{UserId=user.Id,SessionId=session,Token="fixture",ExpiresAt=DateTime.UtcNow.AddHours(1)});
        await db.SaveChangesAsync();
        var identities=new MessengerIdentityService(db,TimeProvider.System,h);
        var ingress=new MessengerIngress(db,identities,TimeProvider.System);
        var tg=new MessengerRealm("telegram","123");var vk=new MessengerRealm("vk","456");
        var secret=new string('x',32);var now=DateTimeOffset.UtcNow;
        void Check(bool ok,string label){if(!ok)throw new Exception("Messenger ingress: "+label);}
        JsonElement Telegram(string text,long id=100,string chat="private",bool bot=false,long? date=null)=>
            JsonSerializer.SerializeToElement(new{update_id=id,message=new{date=date??now.ToUnixTimeSeconds(),text,
                from=new{id=42,is_bot=bot},chat=new{id=42,type=chat}}});
        JsonElement Vk(string text,long group=456,long peer=42)=>JsonSerializer.SerializeToElement(new{type="message_new",group_id=group,
            secret,event_id="event-1",@object=new{message=new{date=now.ToUnixTimeSeconds(),text,from_id=42,peer_id=peer,@out=0}}});
        var code=(await identities.Start(user.Id,session,tg,"password"))!;
        var payload=Telegram("/start "+code);
        Check(MessengerWebhookProof.Telegram(tg,secret,"wrong",payload,now) is null,"secret");
        Check(MessengerWebhookProof.Telegram(tg,secret,secret,Telegram("/link "+code,chat:"group"),now) is null,"group");
        Check(MessengerWebhookProof.Telegram(tg,secret,secret,Telegram("/link "+code,bot:true),now) is null,"bot");
        Check(MessengerWebhookProof.Telegram(tg,secret,secret,Telegram("/link "+code,date:now.AddMinutes(-11).ToUnixTimeSeconds()),now) is null,"stale");
        using var duplicate=JsonDocument.Parse("""{"update_id":100,"update_id":101}""");
        Check(MessengerWebhookProof.Telegram(tg,secret,secret,duplicate.RootElement,now) is null,"duplicate JSON key");
        var e=MessengerWebhookProof.Telegram(tg,secret,secret,payload,now)!;
        Check(await ingress.Process(tg,e)==MessengerIngressStatus.LinkStaged,"stage");
        Check(await ingress.Process(tg,e)==MessengerIngressStatus.Duplicate,"dedup");
        Check(await identities.Resolve(tg,e.Sender) is null,"not automatically linked");
        var proof=(await identities.Candidate(user.Id,session,tg,code))!;
        Check(await identities.Confirm(user.Id,session,tg,code,proof,"password")==ExternalIdentityLinkResult.Linked,"confirmation");
        var status=MessengerWebhookProof.Telegram(tg,secret,secret,Telegram("/status",101),now)!;
        Check(await ingress.Process(tg,status)==MessengerIngressStatus.Linked,"status");
        var vkCode=(await identities.Start(user.Id,session,vk,"password"))!;
        Check(MessengerWebhookProof.Vk(vk,secret,Vk("/link "+vkCode,group:999),now) is null,"wrong community");
        Check(MessengerWebhookProof.Vk(vk,secret,Vk("/link "+vkCode,peer:2000000001),now) is null,"group chat");
        var ve=MessengerWebhookProof.Vk(vk,secret,Vk("/link "+vkCode),now)!;
        Check(await ingress.Process(vk,ve)==MessengerIngressStatus.LinkStaged,"VK stage");
        Check(await ingress.Process(vk,ve)==MessengerIngressStatus.Duplicate,"VK dedup");
        Check(await db.MessengerEventReceipts.CountAsync()==3,"durable receipts");
        Console.WriteLine("Messenger webhook proof, private chat, replay receipt and linking passed");
    }
}
