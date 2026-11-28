using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Services;

internal static class ChatRpConsentSmoke
{
    private sealed class Clock(DateTimeOffset start):TimeProvider
    {
        public DateTimeOffset Now=start;
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    private sealed class Fixture:HttpMessageHandler
    {
        public string Body="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Body=await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK){Content=new StringContent("{\"ok\":true}")};
        }
    }
    public static async Task Run()
    {
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE chat_rp_consents");
        await AccountSecuritySchema.UpgradeAsync(db,default);await AccountSecuritySchema.UpgradeAsync(db,default);
        var clock=new Clock(DateTimeOffset.UtcNow);
        var runtime=new ChatRpRuntime(db,clock);var service=new ChatRpConsentService(db,runtime,clock);
        var realm=new MessengerRealm("telegram","123");
        var e=new ChatRpEvent(realm,"-1001","consent-1",clock.Now,new("42","Алиса"),new("43","Боб"),ChatRpAction.Kiss);
        var policy=new ChatRpGroupPolicy(realm,e.Peer,true,new HashSet<string>{"42","43","44"});
        void Check(bool ok,string label){if(!ok)throw new Exception("RP consent: "+label);}
        Check(await runtime.Queue(e,policy) is null,"no direct consent bypass");
        var invite=(await service.Propose(e,policy))!;
        Check(await service.Propose(e,policy) is null,"duplicate prompt");
        Check(await db.ChatRpDeliveries.CountAsync()==0,"no action before decision");
        var f=new Fixture();using var outbound=new ChatRpOutbound(f);
        Check(await outbound.Invite(e,invite,"123:"+new string('t',32))&&f.Body.Contains("reply_markup")&&
            WebUtility.UrlDecode(f.Body).Contains("rp:a:"+invite.Id.ToString("N")),"Telegram inline controls");
        var secret=new string('s',32);
        JsonElement Update(long sender=43,long peer=-1001)=>JsonSerializer.SerializeToElement(new{
            callback_query=new{id="callback",data="rp:a:"+invite.Id.ToString("N"),from=new{id=sender,is_bot=false},
                message=new{date=clock.Now.ToUnixTimeSeconds(),from=new{id=123},chat=new{id=peer,type="supergroup"}}}});
        var decision=ChatRpGroupProof.TelegramDecision(realm,secret,secret,Update(),clock.Now)!;
        Check(decision.SenderId=="43"&&decision.Id==invite.Id&&decision.Accept,"verified callback");
        Check(ChatRpGroupProof.TelegramDecision(realm,secret,"wrong",Update(),clock.Now) is null,"secret");
        Check(await service.Decide(decision with {SenderId="44"},policy) is null,"wrong target");
        Check(await service.Decide(decision with {Peer="-1002"},policy with {Peer="-1002"}) is null,"wrong group");
        var accepted=(await service.Decide(decision,policy))!;
        Check(accepted.Event.Action==ChatRpAction.Kiss&&await db.ChatRpDeliveries.CountAsync()==1,"consent queued");
        Check(await service.Decide(decision,policy) is null,"one use");
        Check(await runtime.Claim(accepted.DeliveryId,policy) is not null,"claim");
        var decline=(await service.Propose(e with {EventId="consent-2"},policy))!;
        Check(await service.Decide(decision with {Id=decline.Id,Accept=false},policy) is null,"decline");
        Check(await service.Decide(decision with {Id=decline.Id},policy) is null,"decline terminal");
        var expiry=(await service.Propose(e with {EventId="consent-3"},policy))!;
        clock.Now=clock.Now.AddMinutes(3);
        Check(await service.Decide(decision with {Id=expiry.Id},policy) is null,"expiry");
        await service.Cleanup();
        Check(await db.ChatRpConsents.AsNoTracking().AllAsync(x=>x.Consumed&&x.ActorId==""&&x.TargetId==""),"redaction");
        Console.WriteLine("RP consent exact target, group, expiry, replay and inline keyboard checks passed");
    }
}
