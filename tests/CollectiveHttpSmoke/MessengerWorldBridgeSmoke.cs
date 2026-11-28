using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Security;
using Models.Autorization.Services;
using MysticEgo.Api.Orchestra;
using MysticEgo.Soul;

internal static class MessengerWorldBridgeSmoke
{
    private sealed class Hasher : IPasswordHasher
    {
        public string Hash(string password)=>"test:"+password;
        public bool Verify(string password,string hash)=>Hash(password)==hash;
    }
    public static async Task Run()
    {
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        var a=new User{Login="bridge-a",PasswordHash="test:password"};
        var b=new User{Login="bridge-b",PasswordHash="test:password"};
        db.Users.AddRange(a,b);await db.SaveChangesAsync();
        var store=new ExternalIdentityStore(db);
        var realm=new MessengerRealm("telegram","123");
        var key=new ExternalIdentityKey("telegram","bot:123","42");
        await store.RecordVerifiedLink(a.Id,key);
        await store.RecordVerifiedLink(b.Id,new("telegram","bot:123","43"));
        var identity=new MessengerIdentityService(db,TimeProvider.System,new Hasher());
        var world=new SpiritualCommunicationGateway(TimeProvider.System);
        var bridge=new MessengerWorldBridge(identity,world,TimeProvider.System);
        var secret=new string('s',32);var now=DateTimeOffset.UtcNow;long sequence=0;
        void Check(bool ok,string label){if(!ok)throw new Exception("Messenger world bridge: "+label);}
        PrivateMessengerEvent Event(string text,long user=42)=>MessengerWebhookProof.Telegram(realm,secret,secret,
            JsonSerializer.SerializeToElement(new{update_id=++sequence,message=new{date=now.ToUnixTimeSeconds(),text,
                from=new{id=user,is_bot=false},chat=new{id=user,type="private"}}}),now)!;
        WorldPlacement P(string id,Guid owner,string area)=>new(new(id,"world",area,SpiritualParticipantKind.Player),owner);
        WorldPlacement N(string id,string area)=>new(new(id,"world",area,SpiritualParticipantKind.Npc),null);
        SpiritualWorldSnapshot Snapshot(string npcArea="town",bool friend=true)=>new(
            [P("actor-internal",a.Id,"town"),P("friend-internal",b.Id,"forest"),N("npc-internal",npcArea),
                N("hidden-internal","town"),N("remote-internal","forest")],
            [new(a.Id,"actor-internal"),new(b.Id,"friend-internal")],
            [new("actor-internal","npc-internal"),new("actor-internal","friend-internal"),new("actor-internal","remote-internal")],
            friend?[new("actor-internal","friend-internal")]:[])
            {ContactLabels=[new("npc-internal","Хранитель"),new("friend-internal","Друг"),new("hidden-internal","Скрытый"),new("remote-internal","Дальний")]};
        void Publish(SpiritualWorldSnapshot s)=>Check(world.TryPublish(world.Revision,s,out _),"publish");
        Check((await bridge.Handle(realm,Event("/nearby")))?.Text=="Доступных собеседников нет.","empty world");
        Publish(Snapshot());
        Check((await bridge.Handle(realm,Event("/nearby",99)))?.Text.StartsWith("Подключите")==true,"unlinked");
        var nearby=(await bridge.Handle(realm,Event("/nearby")))!;
        Check(nearby.Text.Contains("Хранитель")&&!nearby.Text.Contains("Друг")&&!nearby.Text.Contains("Скрытый")&&
            !nearby.Text.Contains("Дальний")&&!nearby.Text.Contains("internal"),"local filtered discovery");
        var code=nearby.Text.Split('\n')[1].Split(' ')[0];
        Check((await bridge.Handle(realm,Event("/talk "+code+" hello",43)))?.QueuedMessageId is null,"other account handle denied");
        var e=Event("/talk "+code+" Привет");
        var accepted=(await bridge.Handle(realm,e))!;
        Check(accepted.QueuedMessageId is not null,"accepted");
        Check((await bridge.Handle(realm,e))?.QueuedMessageId==accepted.QueuedMessageId,"stable event id");
        Check(world.ProcessNext()&&!world.ProcessNext(),"single world message");
        var committed=world.Committed(accepted.QueuedMessageId!.Value);
        Check(committed?.SenderId=="actor-internal"&&committed.RecipientId=="npc-internal"&&
            committed.Text=="Привет"&&committed.Medium=="spiritual_connection","world envelope");
        var list=(await bridge.Handle(realm,Event("/friends")))!;
        Check(list.Text.Contains("Друг")&&!list.Text.Contains("Хранитель"),"remote confirmed friend");
        var friendCode=list.Text.Split('\n')[1].Split(' ')[0];
        Publish(Snapshot(friend:false));
        Check((await bridge.Handle(realm,Event("/talk "+friendCode+" test")))?.QueuedMessageId is null,"stale friendship");
        var next=(await bridge.Handle(realm,Event("/nearby")))!;
        var npcCode=next.Text.Split('\n')[1].Split(' ')[0];
        Publish(Snapshot(npcArea:"forest"));
        Check((await bridge.Handle(realm,Event("/talk "+npcCode+" test")))?.QueuedMessageId is null,"stale movement");
        Publish(Snapshot());
        var fresh=(await bridge.Handle(realm,Event("/nearby")))!;
        var freshCode=fresh.Text.Split('\n')[1].Split(' ')[0];
        await db.ExternalIdentities.Where(i=>i.Provider==key.Provider&&i.Realm==key.Realm&&i.Subject==key.Subject).ExecuteDeleteAsync();
        Check((await bridge.Handle(realm,Event("/talk "+freshCode+" test")))?.QueuedMessageId is null,"revoked bot link");
        Check(await bridge.Handle(realm,Event("/link secret")) is null,"link commands remain separate");
        Console.WriteLine("Messenger world bridge discovery, selection, identity and routing checks passed");
    }
}
