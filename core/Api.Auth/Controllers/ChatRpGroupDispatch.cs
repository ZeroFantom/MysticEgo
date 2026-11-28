using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Explicit deployment allowlist only. No automatic group registration from messages.</summary>
public sealed class ChatRpGroupDispatch(IConfiguration configuration,ChatRpRuntime runtime,ChatRpOutbound outbound,ChatRpConsentService consent,ChatRpPreferences preferences,ChatRpMembership membership)
{
    private ChatRpGroupPolicy? Baseline(MessengerRealm realm,string peer)
    {
        if(!configuration.GetValue<bool>("Messenger:GroupRp:Enabled"))return null;
        var current=MessengerConfiguration.Read(configuration,realm.Provider);
        if(current is null||current.Value.Realm!=realm)return null;
        var section=configuration.GetSection("Messenger:GroupRp:Groups")
            .GetChildren().Take(257).Where(s=>s["Provider"]==realm.Provider&&s["BotId"]==realm.BotId&&s["Peer"]==peer).ToArray();
        if(section.Length!=1||!section[0].GetValue<bool>("Enabled"))return null;
        var ids=section[0].GetSection("Participants").Get<string[]>()??[];
        if(ids.Length>1000)return null;
        var definitions=section[0].GetSection("CustomActions").GetChildren().Take(65).ToArray();
        ChatRpCustomCatalog custom;
        try
        {
            custom=new(definitions.Select(s=>
            {
                var category=s["Category"];
                var definition=ChatRpCatalog.All.SingleOrDefault(a=>a.Category==category);
                if(definition is null)throw new ArgumentException("Unknown RP category.");
                return new ChatRpCustomAction(s["Command"]??"",definition.Action,s["Template"]??"");
            }));
        }
        catch(ArgumentException){custom=ChatRpCustomCatalog.Empty;}
        return new(realm,peer,true,ids.ToHashSet(StringComparer.Ordinal)){CustomActions=custom};
    }

    public ChatRpCustomAction? ResolveCustom(MessengerRealm realm,string peer,string? text)=>
        Baseline(realm,peer)?.CustomActions.Find(text);

    private async Task<ChatRpGroupPolicy?> Policy(MessengerRealm realm,string peer,CancellationToken ct)
    {
        var baseline=Baseline(realm,peer);
        return baseline is null?null:await preferences.Effective(baseline,ct);
    }
    private Task<ChatRpGroupPolicy?> Policy(ChatRpEvent e,CancellationToken ct)=>Policy(e.Realm,e.Peer,ct);

    public async Task Control(ChatRpControlEvent e,CancellationToken ct)
    {
        var baseline=Baseline(e.Realm,e.Peer);
        if(baseline is null)return;
        var token=MessengerConfiguration.ReplyToken(configuration,e.Realm);
        if(string.IsNullOrEmpty(token))return;
        var role=e.Control==ChatRpControl.MemberOff?ChatRpMemberRole.Unknown:
            await membership.Role(e.Realm,e.Peer,e.SenderId,token,ct);
        // Telegram getChatMember for other users is guaranteed only for admin bots.
        if(e.Control is ChatRpControl.GroupEnable or ChatRpControl.GroupDisable && e.Realm.Provider=="telegram"&&
            await membership.Role(e.Realm,e.Peer,e.Realm.BotId,token,ct)!=ChatRpMemberRole.Admin)return;
        if(await preferences.Apply(e,baseline,role,ct))
            await outbound.ControlReply(e,token,ct);
    }

    private async Task<bool> Present(ChatRpEvent e,string token,CancellationToken ct)
    {
        var roles=await Task.WhenAll(membership.Role(e.Realm,e.Peer,e.Actor.Id,token,ct),
            membership.Role(e.Realm,e.Peer,e.Target.Id,token,ct));
        return roles.All(r=>r is ChatRpMemberRole.Admin or ChatRpMemberRole.Member);
    }

    public async Task Process(ChatRpEvent e,CancellationToken ct)
    {
        var policy=await Policy(e,ct);
        if(policy is null)return;
        // Media and outbound are separately enabled. A disabled reply path queues nothing.
        var token=MessengerConfiguration.ReplyToken(configuration,e.Realm);
        if(string.IsNullOrEmpty(token)||!policy.Allows(e)||!await Present(e,token,ct))return;
        if(ChatRpCatalog.Find(e.Action) is {RequiresConsent:true})
        {
            var invite=await consent.Propose(e,policy,ct);
            var live=await Policy(e,ct);token=MessengerConfiguration.ReplyToken(configuration,e.Realm);
            if(invite is not null&&live is not null&&live.Allows(e)&&!string.IsNullOrEmpty(token))
                await outbound.Invite(e,invite,token,ct);
            return;
        }
        var id=await runtime.Queue(e,policy,ct);
        if(id is Guid value)await Deliver(e,value,policy,ct);
    }

    public async Task Decide(ChatRpDecision d,CancellationToken ct)
    {
        var token=MessengerConfiguration.ReplyToken(configuration,d.Realm);
        if(d.CallbackId is string callback&&d.Realm.Provider=="telegram")
            await outbound.Acknowledge(d.Realm,callback,token,ct);
        var policy=await Policy(d.Realm,d.Peer,ct);
        if(policy is null||string.IsNullOrEmpty(token))return;
        var result=await consent.Decide(d,policy,ct);
        if(d.Realm.Provider=="vk"&&d.CallbackId is not null)await outbound.AcknowledgeVk(d,token,ct);
        if(result is not null)await Deliver(result.Event,result.DeliveryId,policy,ct);
    }

    private async Task Deliver(ChatRpEvent e,Guid id,ChatRpGroupPolicy policy,CancellationToken ct)
    {
        var current=await Policy(e,ct)??policy with {Enabled=false};
        var row=await runtime.Claim(id,current,ct);
        if(row is null)return;
        ChatRpMedia? media=null;
        var category=row.Category;
        // Only owned/licensed approved assets; no nekos.best rehosting or arbitrary fetch.
        if(e.Realm.Provider=="telegram")
        {
            var host=configuration["Messenger:GroupRp:MediaHost"];
            var items=configuration.GetSection("Messenger:GroupRp:Media").GetChildren().Take(4097).ToArray();
            if(!string.IsNullOrEmpty(host)&&items.Length<=4096)
            {
                try
                {
                    var assets=items.Select(s=>new ChatRpApprovedAsset(s["Category"]??"",
                        new Uri(s["Url"]??"",UriKind.Absolute),s["Attribution"]??""));
                    media=await new ApprovedChatRpMedia(host,assets).Find(category,ct);
                }
                catch(Exception ex) when(ex is ArgumentException or UriFormatException){}
            }
        }
        var vk=ChatRpVkMediaLibrary.Pick(configuration.GetSection("Messenger:GroupRp:VkMedia:"+category)
            .GetChildren().Take(257).Select(s=>s.Value??""),configuration["Messenger:GroupRp:VkAttachments:"+category]);
        // Recheck live group opt-in and bot configuration immediately before sending.
        current=await Policy(e,ct);
        var token=MessengerConfiguration.ReplyToken(configuration,e.Realm);
        if(current is null||!current.Allows(e)||string.IsNullOrEmpty(token)||!await Present(e,token,ct))
        {await runtime.Finish(id,ChatRpDeliveryState.Rejected,ct);return;}
        var sent=await outbound.Send(row,token,media,vk,ct);
        await runtime.Finish(id,sent?ChatRpDeliveryState.Sent:ChatRpDeliveryState.Uncertain,ct);
    }
}
