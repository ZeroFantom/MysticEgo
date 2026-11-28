using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class ChatRpRuntimeSmoke
{
    private sealed class Fixture:HttpMessageHandler
    {
        public int Calls;
        public string Path="",Body="";
        public string Response="{\"ok\":true}";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Path=request.RequestUri!.AbsolutePath;Body=await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK){Content=new StringContent(Response,Encoding.UTF8,"application/json")};
        }
    }
    public static async Task Run()
    {
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE chat_rp_deliveries");
        await AccountSecuritySchema.UpgradeAsync(db,default);await AccountSecuritySchema.UpgradeAsync(db,default);
        var runtime=new ChatRpRuntime(db,TimeProvider.System);
        var realm=new MessengerRealm("telegram","123");
        var e=new ChatRpEvent(realm,"-1001","event-1",DateTimeOffset.UtcNow,new("42","Алиса"),new("43","Боб"),ChatRpAction.Hug);
        var policy=new ChatRpGroupPolicy(realm,e.Peer,true,new HashSet<string>{"42","43"});
        void Check(bool ok,string label){if(!ok)throw new Exception("Group RP runtime: "+label);}
        Check(await runtime.Queue(e,policy with {Enabled=false}) is null,"disabled");
        Check(await runtime.Queue(e,policy with {Participants=new HashSet<string>{"42"}}) is null,"target opt-in");
        Check(await runtime.Queue(e with {Action=ChatRpAction.Kiss},policy) is null,"sensitive action denied");
        Check(await runtime.Queue(e with {Peer="42"},policy with {Peer="42"}) is null,"not group");
        var id=(await runtime.Queue(e,policy))!.Value;
        Check(await runtime.Queue(e,policy) is null,"durable duplicate");
        var row=(await runtime.Claim(id,policy))!;
        Check(await runtime.Claim(id,policy) is null,"single claim");
        var f=new Fixture();using var outbound=new ChatRpOutbound(f);
        var token="123:"+new string('t',32);
        var image=new ChatRpMedia(new Uri("https://media.example.invalid/hug.gif"),"Approved");
        Check(await outbound.Send(row,token,image,null)&&f.Path.EndsWith("/sendAnimation")&&f.Body.Contains("chat_id=-1001")&&f.Body.Contains("animation="),"telegram GIF");
        Check(await runtime.Finish(id,ChatRpDeliveryState.Sent),"finish");
        Check(await runtime.Queue(e,policy) is null,"no resend");
        Check(await db.ChatRpDeliveries.AsNoTracking().AnyAsync(x=>x.Id==id&&x.Caption==""&&x.ActorId==""&&x.TargetId==""),"redaction");
        var second=(await runtime.Queue(e with {EventId="event-2"},policy))!.Value;
        Check(await runtime.Claim(second,policy with {Enabled=false}) is null,"revoked before claim");
        var vk=new MessengerRealm("vk","456");
        var ve=e with {Realm=vk,Peer="2000000001",EventId="vk-1"};
        var vp=new ChatRpGroupPolicy(vk,ve.Peer,true,policy.Participants);
        var vi=(await runtime.Queue(ve,vp))!.Value;var vr=(await runtime.Claim(vi,vp))!;
        f.Response="{\"response\":1}";
        Check(await outbound.Send(vr,new string('v',32),null,"doc-456_99")&&f.Path=="/method/messages.send"&&f.Body.Contains("attachment=doc-456_99"),"VK native attachment");
        Check(await runtime.Finish(vi,ChatRpDeliveryState.Uncertain)&&await runtime.Claim(vi,vp) is null,"uncertain never retried");
        Console.WriteLine("Group RP opt-in, durable single attempt, Telegram GIF and VK native attachment checks passed");
    }
}
