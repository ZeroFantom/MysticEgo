using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

internal static class ChatRpManagementSmoke
{
    private sealed class Fixture:HttpMessageHandler
    {
        public string Body="";
        public string Response="";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Body=await request.Content!.ReadAsStringAsync(ct);
            return new(HttpStatusCode.OK){Content=new StringContent(Response)};
        }
    }
    public static async Task Run()
    {
        var options=new DbContextOptionsBuilder<AuthDbContext>().UseSqlite("Data Source=:memory:;Foreign Keys=True").Options;
        await using var db=new AuthDbContext(options);
        await db.Database.OpenConnectionAsync();await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("DROP TABLE chat_rp_preferences");
        await AccountSecuritySchema.UpgradeAsync(db,default);await AccountSecuritySchema.UpgradeAsync(db,default);
        var prefs=new ChatRpPreferences(db,TimeProvider.System);
        var runtime=new ChatRpRuntime(db,TimeProvider.System);
        var consent=new ChatRpConsentService(db,runtime,TimeProvider.System);
        var realm=new MessengerRealm("telegram","123");
        var now=DateTimeOffset.UtcNow;var secret=new string('s',32);
        var baseline=new ChatRpGroupPolicy(realm,"-1001",true,new HashSet<string>{"42","43"});
        void Check(bool ok,string label){if(!ok)throw new Exception("RP management: "+label);}
        var payload=JsonSerializer.SerializeToElement(new{update_id=123,message=new{date=now.ToUnixTimeSeconds(),
            text="/rp_disable",from=new{id=42,is_bot=false},chat=new{id=-1001,type="supergroup"}}});
        var command=ChatRpGroupProof.TelegramControl(realm,secret,secret,payload,now)!;
        Check(command.Control==ChatRpControl.GroupDisable,"verified command");
        Check(ChatRpGroupProof.TelegramControl(realm,secret,"wrong",payload,now) is null,"secret");
        Check(!await prefs.Apply(command,baseline,ChatRpMemberRole.Member),"member cannot disable group");
        var event1=new ChatRpEvent(realm,baseline.Peer,"management-1",now,new("42","A"),new("43","B"),ChatRpAction.Kiss);
        var invite=await consent.Propose(event1,baseline);
        Check(invite is not null,"pending invite");
        var pending=await runtime.Queue(event1 with {Action=ChatRpAction.Hug,EventId="management-2"},baseline);
        Check(pending is not null,"pending action");
        Check(await prefs.Apply(command,baseline,ChatRpMemberRole.Admin),"admin disable");
        Check(!(await prefs.Effective(baseline)).Enabled,"disabled");
        Check(await db.ChatRpConsents.AsNoTracking().AllAsync(x=>x.Consumed&&x.ActorId==""),"invalidated invitation");
        Check(await db.ChatRpDeliveries.AsNoTracking().AllAsync(x=>x.Status==ChatRpDeliveryState.Rejected),"invalidated pending");
        var enable=command with {Control=ChatRpControl.GroupEnable,Occurred=command.Occurred.AddSeconds(1)};
        Check(await prefs.Apply(enable,baseline,ChatRpMemberRole.Admin),"admin enable");
        Check(!await prefs.Apply(command,baseline,ChatRpMemberRole.Admin),"stale event");
        Check(!(await prefs.Effective(baseline with {Enabled=false})).Enabled,"operator ceiling");
        var off=command with {Control=ChatRpControl.MemberOff,SenderId="43",Occurred=now};
        Check(await prefs.Apply(off,baseline,ChatRpMemberRole.Unknown),"self off");
        Check(!(await prefs.Effective(baseline)).Participants.Contains("43"),"personal opt-out");
        var on=off with {Control=ChatRpControl.MemberOn,Occurred=now.AddSeconds(1)};
        Check(!await prefs.Apply(on,baseline,ChatRpMemberRole.Unknown),"on requires membership");
        Check(await prefs.Apply(on,baseline,ChatRpMemberRole.Member),"self on");
        Check((await prefs.Effective(baseline)).Participants.Contains("43"),"opt in restored");
        Check(!await prefs.Apply(on with {SenderId="44"},baseline,ChatRpMemberRole.Member),"no expansion beyond allowlist");
        var f=new Fixture{Response="""{"ok":true,"result":{"user":{"id":42},"status":"administrator"}}"""};
        using var members=new ChatRpMembership(f);
        var token="123:"+new string('t',32);
        Check(await members.Role(realm,"-1001","42",token)==ChatRpMemberRole.Admin,"telegram admin");
        Check(await members.Role(realm,"-1001","43",token)==ChatRpMemberRole.Unknown,"mismatched user");
        f.Response="""{"response":{"items":[{"member_id":42,"is_admin":true},{"member_id":43}]}}""";
        var vk=new MessengerRealm("vk","456");
        Check(await members.Role(vk,"2000000001","42",new string('v',32))==ChatRpMemberRole.Admin,"vk admin");
        Check(await members.Role(vk,"2000000001","43",new string('v',32))==ChatRpMemberRole.Member,"vk member");
        Check(await members.Role(vk,"2000000001","44",new string('v',32))==ChatRpMemberRole.Unknown,"vk absent");
        Console.WriteLine("RP group administrator, self preference, stale command and revocation checks passed");
    }
}
