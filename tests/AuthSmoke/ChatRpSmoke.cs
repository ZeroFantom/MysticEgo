using System.Net;
using System.Text;
using System.Text.Json;
using Models.Autorization.Services;

internal static class ChatRpSmoke
{
    private sealed class Fixture:HttpMessageHandler
    {
        public int Calls;
        public string Body="";
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            if(request.RequestUri?.AbsoluteUri!="https://nekos.best/api/v2/hug")throw new Exception("Unexpected media request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Body,Encoding.UTF8,"application/json")});
        }
    }
    public static async Task Run()
    {
        void Check(bool ok,string label){if(!ok)throw new Exception("Chat RP: "+label);}
        var realm=new MessengerRealm("telegram","123");var secret=new string('s',32);var now=DateTimeOffset.UtcNow;
        JsonElement Event(string text="обнять",long peer=-1001,long targetPeer=-1001,bool bot=false,long target=43)=>
            JsonSerializer.SerializeToElement(new{update_id=123,message=new{date=now.ToUnixTimeSeconds(),text,
                from=new{id=42,is_bot=false,first_name="Алиса"},chat=new{id=peer,type="supergroup"},
                reply_to_message=new{from=new{id=target,is_bot=bot,first_name="Боб"},chat=new{id=targetPeer,type="supergroup"}}}});
        var e=ChatRpGroupProof.Telegram(realm,secret,secret,Event(),now)!;
        Check(e.Action==ChatRpAction.Hug&&e.Actor.Name=="Алиса"&&e.Target.Name=="Боб","reply command");
        Check(ChatRpGroupProof.Telegram(realm,secret,"wrong",Event(),now) is null,"secret");
        Check(ChatRpGroupProof.Telegram(realm,secret,secret,Event(targetPeer:-1002),now) is null,"cross chat");
        Check(ChatRpGroupProof.Telegram(realm,secret,secret,Event(bot:true),now) is null,"bot target");
        Check(ChatRpGroupProof.Telegram(realm,secret,secret,Event(target:42),now) is null,"self");
        Check(ChatRpGroupProof.Telegram(realm,secret,secret,Event("обычный разговор"),now) is null,"ordinary chat ignored");
        var vk=new MessengerRealm("vk","456");
        var v=JsonSerializer.SerializeToElement(new{type="message_new",group_id=456,secret,event_id="rp-vk",
            @object=new{message=new{date=now.ToUnixTimeSeconds(),text="/hug",from_id=42,peer_id=2000000001,@out=0,
                reply_message=new{from_id=43,peer_id=2000000001}}}});
        Check(ChatRpGroupProof.Vk(vk,secret,v,now)?.Action==ChatRpAction.Hug,"VK reply");
        var renderer=new ChatRpRenderer(new ApprovedChatRpMedia("media.example.invalid",
            [new("hug",new Uri("https://media.example.invalid/hug.gif"),"Approved fixture")]));
        Check(await renderer.Render(e,false,true,false) is null,"group opt-in");
        Check(await renderer.Render(e,true,false,false) is null,"blocked pair");
        var card=(await renderer.Render(e,true,true,false))!;
        Check(card.Text=="Алиса обнимает участника «Боб»."&&card.Media?.Url.Host=="media.example.invalid","render and image");
        var kiss=e with {Action=ChatRpAction.Kiss};
        Check((await renderer.Render(kiss,true,true,false)) is {RequiresConsent:true,Media:null},"consent pending");
        Check((await renderer.Render(kiss,true,true,true)) is {RequiresConsent:false,Media:null},"text fallback");
        Check(ChatRpCatalog.Parse("/rp дать пять")?.Action==ChatRpAction.HighFive,"multiword alias");
        var f=new Fixture{Body="""{"results":[{"url":"https://nekos.best/api/v2/hug/3e8f2a1b-9c4d-4f7e-8a2b-6d5c4e3f2a1b.gif","anime_name":"Fixture"}]}"""};
        using var source=new NekosChatRpMedia(true,f);
        Check(await source.Find("https://example.invalid") is null&&f.Calls==0,"no arbitrary search");
        Check((await source.Find("hug"))?.Attribution=="Anime: Fixture · nekos.best","media result");
        f.Body="""{"results":[{"url":"https://evil.invalid/api/v2/hug/3e8f2a1b-9c4d-4f7e-8a2b-6d5c4e3f2a1b.gif","anime_name":"Fixture"}]}""";
        Check(await source.Find("hug") is null,"host allowlist");
        Console.WriteLine("Social RP reply scope, action catalog, consent and approved anime media checks passed");
    }
}
