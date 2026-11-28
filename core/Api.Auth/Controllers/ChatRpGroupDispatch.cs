using Models.Autorization.Domain.Entities;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>Explicit deployment allowlist only. No automatic group registration from messages.</summary>
public sealed class ChatRpGroupDispatch(IConfiguration configuration,ChatRpRuntime runtime,ChatRpOutbound outbound)
{
    private ChatRpGroupPolicy? Policy(ChatRpEvent e)
    {
        if(!configuration.GetValue<bool>("Messenger:GroupRp:Enabled"))return null;
        var current=MessengerConfiguration.Read(configuration,e.Realm.Provider);
        if(current is null||current.Value.Realm!=e.Realm)return null;
        var section=configuration.GetSection("Messenger:GroupRp:Groups")
            .GetChildren().Take(257).Where(s=>s["Provider"]==e.Realm.Provider&&s["BotId"]==e.Realm.BotId&&s["Peer"]==e.Peer).ToArray();
        if(section.Length!=1||!section[0].GetValue<bool>("Enabled"))return null;
        var ids=section[0].GetSection("Participants").Get<string[]>()??[];
        if(ids.Length>1000)return null;
        return new(e.Realm,e.Peer,true,ids.ToHashSet(StringComparer.Ordinal));
    }

    public async Task Process(ChatRpEvent e,CancellationToken ct)
    {
        var policy=Policy(e);
        if(policy is null)return;
        // Media and outbound are separately enabled. A disabled reply path queues nothing.
        var token=MessengerConfiguration.ReplyToken(configuration,e.Realm);
        if(string.IsNullOrEmpty(token))return;
        var id=await runtime.Queue(e,policy,ct);
        if(id is null)return;
        var current=Policy(e)??policy with {Enabled=false};
        var row=await runtime.Claim(id.Value,current,ct);
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
        var vk=configuration["Messenger:GroupRp:VkAttachments:"+category];
        // Recheck live group opt-in and bot configuration immediately before sending.
        current=Policy(e);
        token=MessengerConfiguration.ReplyToken(configuration,e.Realm);
        if(current is null||!current.Allows(e)||string.IsNullOrEmpty(token))
        {await runtime.Finish(id.Value,ChatRpDeliveryState.Rejected,ct);return;}
        var sent=await outbound.Send(row,token,media,vk,ct);
        await runtime.Finish(id.Value,sent?ChatRpDeliveryState.Sent:ChatRpDeliveryState.Uncertain,ct);
    }
}
