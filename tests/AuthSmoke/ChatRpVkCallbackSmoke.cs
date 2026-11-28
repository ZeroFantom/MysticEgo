using System.Net;
using System.Text.Json;
using Models.Autorization.Services;

internal static class ChatRpVkCallbackSmoke
{
    private sealed class Fixture:HttpMessageHandler
    {
        public string Body="",Path="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Path=request.RequestUri!.AbsolutePath;Body=WebUtility.UrlDecode(await request.Content!.ReadAsStringAsync(ct));
            return new(HttpStatusCode.OK){Content=new StringContent("{\"response\":1}")};
        }
    }
    public static async Task Run()
    {
        var realm=new MessengerRealm("vk","456");var secret=new string('s',32);var now=DateTimeOffset.UtcNow;
        var id=Guid.NewGuid();var handle="rp:a:"+id.ToString("N");
        JsonElement Update(string command,long peer=2000000001,long user=43,long group=456)=>
            JsonSerializer.SerializeToElement(new{type="message_event",group_id=group,secret,event_id="update",
                @object=new{event_id="callback",peer_id=peer,user_id=user,payload=new{rp=command}}});
        void Check(bool ok,string label){if(!ok)throw new Exception("VK RP callback: "+label);}
        var decision=ChatRpGroupProof.VkDecision(realm,secret,Update(handle),now)!;
        Check(decision.Id==id&&decision.Accept&&decision.SenderId=="43"&&decision.CallbackId=="callback","parse");
        Check(ChatRpGroupProof.VkDecision(realm,secret,Update(handle,peer:43),now) is null,"private denied");
        Check(ChatRpGroupProof.VkDecision(realm,secret,Update(handle,group:457),now) is null,"wrong community");
        Check(ChatRpGroupProof.VkDecision(realm,secret,Update("invalid"),now) is null,"invalid handle");
        var f=new Fixture();using var outbound=new ChatRpOutbound(f);var token=new string('v',32);
        var e=new ChatRpEvent(realm,"2000000001","fixture",now,new("42","A"),new("43","B"),ChatRpAction.Kiss);
        Check(await outbound.Invite(e,new(id,"Invitation"),token),"invite");
        Check(f.Body.Contains("callback")&&f.Body.Contains(handle),"native callback keyboard");
        await outbound.AcknowledgeVk(decision,token);
        Check(f.Path=="/method/messages.sendMessageEventAnswer"&&f.Body.Contains("user_id=43")&&
            f.Body.Contains("peer_id=2000000001")&&f.Body.Contains("event_id=callback"),"callback ack");
        Console.WriteLine("VK native RP callback keyboard and scope checks passed");
    }
}
