using System.Net;
using System.Text;
using System.Text.Json;
using Models.Autorization.Services;

internal static class MessengerOutboundSmoke
{
    private sealed class Fixture : HttpMessageHandler
    {
        public int Calls;
        public string LastBody="";
        public string LastHost="";
        public string LastPath="";
        public string Response="{\"ok\":true}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;
            LastHost=request.RequestUri!.Host;LastPath=request.RequestUri.AbsolutePath;
            LastBody=await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK){Content=new StringContent(Response,Encoding.UTF8,"application/json")};
        }
    }

    public static async Task Run()
    {
        var f=new Fixture();using var outbound=new MessengerOutbound(f);
        var tg=new MessengerRealm("telegram","123");var vk=new MessengerRealm("vk","456");
        var secret=new string('x',32);var now=DateTimeOffset.UtcNow;
        var token="123:"+new string('t',32);
        var linkCode=new string('c',43);
        var telegram=JsonSerializer.SerializeToElement(new{update_id=123,message=new{date=now.ToUnixTimeSeconds(),text="/link "+linkCode,
            from=new{id=42,is_bot=false},chat=new{id=42,type="private"}}});
        var message=MessengerWebhookProof.Telegram(tg,secret,secret,telegram,now)!;
        void Check(bool ok,string label){if(!ok)throw new Exception("Messenger outbound: "+label);}
        Check(!await outbound.Reply(tg,message,MessengerIngressStatus.Duplicate,token)&&f.Calls==0,"duplicate no send");
        Check(!await outbound.Reply(tg,message,MessengerIngressStatus.Ignored,token)&&f.Calls==0,"ignored no send");
        Check(!await outbound.Reply(tg,message,MessengerIngressStatus.LinkStaged,"124:"+new string('t',32))&&f.Calls==0,"wrong bot token");
        Check(await outbound.Reply(tg,message,MessengerIngressStatus.LinkStaged,token),"telegram response");
        Check(f.LastHost=="api.telegram.org"&&f.LastPath.EndsWith("/sendMessage")&&f.LastBody.Contains("chat_id=42")&&!f.LastBody.Contains(linkCode),"telegram request");
        f.Response="{\"ok\":false}";
        Check(!await outbound.Reply(tg,message,MessengerIngressStatus.Help,token),"telegram application failure");
        var payload=JsonSerializer.SerializeToElement(new{type="message_new",group_id=456,secret,event_id="fixture-event",
            @object=new{message=new{date=now.ToUnixTimeSeconds(),text="/status",from_id=42,peer_id=42,@out=0}}});
        var vkMessage=MessengerWebhookProof.Vk(vk,secret,payload,now)!;
        f.Response="{\"response\":1}";
        Check(await outbound.Reply(vk,vkMessage,MessengerIngressStatus.Unlinked,new string('v',32)),"VK response");
        Check(f.LastHost=="api.vk.com"&&f.LastPath=="/method/messages.send"&&f.LastBody.Contains("peer_id=42")&&f.LastBody.Contains("random_id="),"VK request");
        var previous=f.LastBody;
        Check(await outbound.Reply(vk,vkMessage,MessengerIngressStatus.Unlinked,new string('v',32))&&f.LastBody==previous,"stable VK random_id");
        f.Response="{\"error\":{\"error_code\":5}}";
        Check(!await outbound.Reply(vk,vkMessage,MessengerIngressStatus.Unlinked,new string('v',32)),"VK application failure");
        Console.WriteLine("Messenger outbound fixed destinations, safe replies and provider response checks passed");
    }
}
