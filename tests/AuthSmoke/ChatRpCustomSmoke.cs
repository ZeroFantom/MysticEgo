using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Services;

internal static class ChatRpCustomSmoke
{
    public static async Task Run()
    {
        void Check(bool ok,string label){if(!ok)throw new Exception("Custom RP: "+label);}
        var action=new ChatRpCustomAction("утешить",ChatRpAction.Hug,"{actor} утешает участника «{target}».");
        Check(action.Valid,"valid template");
        Check(!(action with {Command="обнять"}).Valid,"cannot override builtin");
        Check(!(action with {Action=ChatRpAction.Kiss}).Valid,"sensitive category denied");
        Check(!(action with {Template="{actor} {target} https://evil.invalid"}).Valid,"links denied");
        Check(!(action with {Template="{actor} {actor} {target}"}).Valid,"duplicate token");
        Check(!(action with {Template="{actor} {target} {anything}"}).Valid,"unknown token");
        var catalog=new ChatRpCustomCatalog([action]);
        Check(catalog.Find("/rp Утешить")==action,"alias");
        try{_ = new ChatRpCustomCatalog([action,action]);throw new Exception("Duplicate accepted");}catch(ArgumentException){}
        var realm=new MessengerRealm("telegram","123");var now=DateTimeOffset.UtcNow;var secret=new string('s',32);
        JsonElement Event(string command,long peer=-1001)=>JsonSerializer.SerializeToElement(new{update_id=123,
            message=new{date=now.ToUnixTimeSeconds(),text=command,from=new{id=42,is_bot=false,first_name="Алиса"},
                chat=new{id=peer,type="supergroup"},reply_to_message=new{from=new{id=43,is_bot=false,first_name="Боб"},
                chat=new{id=peer,type="supergroup"}}}});
        ChatRpCustomAction? Resolve(MessengerRealm r,string peer,string? text)=>r==realm&&peer=="-1001"?catalog.Find(text):null;
        var e=ChatRpGroupProof.Telegram(realm,secret,secret,Event("/rp утешить"),now,Resolve)!;
        Check(e.Custom==action&&e.Action==ChatRpAction.Hug,"parsed scoped alias");
        Check(ChatRpGroupProof.Telegram(realm,secret,secret,Event("утешить",-1002),now,Resolve) is null,"other chat");
        Check(ChatRpGroupProof.Telegram(realm,secret,"bad",Event("утешить"),now,Resolve) is null,"secret");
        Check(ChatRpGroupProof.Telegram(realm,secret,secret,Event("обнять"),now,Resolve)?.Custom is null,"builtin priority");
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        var runtime=new ChatRpRuntime(db,TimeProvider.System);
        var policy=new ChatRpGroupPolicy(realm,e.Peer,true,new HashSet<string>{"42","43"}){CustomActions=catalog};
        Check(await runtime.Queue(e,policy with {CustomActions=ChatRpCustomCatalog.Empty}) is null,"removed command");
        Check(await runtime.Queue(e with {Action=ChatRpAction.Pat},policy) is null,"action mismatch");
        var id=(await runtime.Queue(e,policy))!.Value;
        var row=(await runtime.Claim(id,policy))!;
        Check(row.Caption=="Алиса утешает участника «Боб»."&&row.Category=="hug","custom caption and category");
        Check(await runtime.Queue(e,policy) is null,"duplicate");
        var vk=new[]{"photo-456_1","doc-456_2"};
        for(var i=0;i<20;i++)Check(vk.Contains(ChatRpVkMediaLibrary.Pick(vk)),"approved random native attachment");
        Check(ChatRpVkMediaLibrary.Pick(["https://evil.invalid"],"doc-456_2") is null,"invalid list fails closed");
        Check(ChatRpVkMediaLibrary.Pick([],"doc-456_2")=="doc-456_2","legacy fallback");
        Console.WriteLine("Custom RP scoped aliases, approved templates and native media collection checks passed");
    }
}
