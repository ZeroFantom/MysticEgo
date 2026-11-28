using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Models.Autorization.Services;

/// <summary>Social chat roleplay only. No world identity, character state, reward or NPC mutation.</summary>
public enum ChatRpAction { Hug, Pat, HighFive, Handshake, Wave, Dance, Poke, Cuddle, Kiss, Slap }
public sealed record ChatRpDefinition(ChatRpAction Action,string Command,string Category,string Verb,bool RequiresConsent);
public sealed record ChatRpActor(string Id,string Name);
public sealed record ChatRpEvent(MessengerRealm Realm,string Peer,string EventId,DateTimeOffset Occurred,
    ChatRpActor Actor,ChatRpActor Target,ChatRpAction Action);
public sealed record ChatRpCard(string Text,string Category,bool RequiresConsent,ChatRpMedia? Media=null);

public static class ChatRpCatalog
{
    private static readonly ChatRpDefinition[] definitions=[
        new(ChatRpAction.Hug,"обнять","hug","обнимает участника",false),
        new(ChatRpAction.Pat,"погладить","pat","гладит по голове участника",false),
        new(ChatRpAction.HighFive,"дать пять","highfive","даёт пять участнику",false),
        new(ChatRpAction.Handshake,"пожать руку","handshake","пожимает руку участнику",false),
        new(ChatRpAction.Wave,"помахать","wave","машет рукой участнику",false),
        new(ChatRpAction.Dance,"танцевать","dance","танцует вместе с участником",false),
        new(ChatRpAction.Poke,"тыкнуть","poke","легонько тыкает участника",false),
        new(ChatRpAction.Cuddle,"прижаться","cuddle","прижимается к участнику",true),
        new(ChatRpAction.Kiss,"поцеловать","kiss","целует участника",true),
        new(ChatRpAction.Slap,"дать пощёчину","slap","даёт пощёчину участнику",true)
    ];
    public static IReadOnlyList<ChatRpDefinition> All=>Array.AsReadOnly(definitions);
    public static ChatRpDefinition? Parse(string? text)
    {
        if(text is null||text.Length>64)return null;
        var value=text.Trim().ToLowerInvariant().Replace('ё','е');
        if(value.StartsWith("/rp ",StringComparison.Ordinal))value=value[4..].Trim();
        else if(value.StartsWith('/'))value=value[1..];
        return definitions.FirstOrDefault(x=>x.Command.Replace('ё','е')==value||x.Category==value);
    }
    public static ChatRpDefinition? Find(ChatRpAction action)=>definitions.FirstOrDefault(x=>x.Action==action);
    internal static string Name(string? input)
    {
        if(string.IsNullOrWhiteSpace(input))return "Участник";
        var sb=new StringBuilder();
        foreach(var rune in input.EnumerateRunes())
        {
            var category=Rune.GetUnicodeCategory(rune);
            if(category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.Surrogate or
                UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)continue;
            // Prevent user-controlled mentions, links and markdown syntax in plain chat captions.
            if(rune.Value is '@' or '<' or '>' or '[' or ']' or '&' or '#' or '*' or '_' or '`' or ':' or '/' or '\\')continue;
            if(sb.Length+rune.Utf16SequenceLength>64)break;
            sb.Append(rune.ToString());
        }
        var result=sb.ToString().Trim();
        return result.Length==0?"Участник":result;
    }
}

/// <summary>Separate group parser: explicit RP command replying to another human in the same chat.
/// Caller must verify opt-in group/admin authority, quotas, blocks, and event receipt before dispatch.</summary>
public static class ChatRpGroupProof
{
    private static bool Obj(JsonElement p,string n,out JsonElement v)=>p.TryGetProperty(n,out v)&&TelegramIdentityProof.UniqueObject(v);
    private static string? Text(JsonElement p,string k)=>TelegramIdentityProof.Text(p,k);
    private static long? Num(JsonElement p,string k)=>p.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt64(out var n)?n:null;
    private static string? Id(JsonElement p,string k)=>Num(p,k) is long n&&n>0?n.ToString(CultureInfo.InvariantCulture):null;
    private static ChatRpActor? TgActor(JsonElement message)
    {
        if(message.TryGetProperty("sender_chat",out _)||!Obj(message,"from",out var from)||
            !from.TryGetProperty("is_bot",out var bot)||bot.ValueKind!=JsonValueKind.False||Id(from,"id") is not string id)return null;
        return new(id,ChatRpCatalog.Name((Text(from,"first_name")??"")+" "+(Text(from,"last_name")??"")));
    }
    private static DateTimeOffset? Date(JsonElement message,DateTimeOffset now)
    {
        var seconds=Num(message,"date");
        if(seconds is null||seconds<0||seconds>253402300799)return null;
        var at=DateTimeOffset.FromUnixTimeSeconds(seconds.Value);
        return MessengerWebhookProof.Fresh(at,now)?at:null;
    }
    private static bool EventId(string? value)=>value is {Length:>0 and <=128}&&value.All(c=>c is >= '!' and <= '~');
    private static ChatRpControl? Control(string? text)=>text switch
    {
        "/rp_enable"=>ChatRpControl.GroupEnable,"/rp_disable"=>ChatRpControl.GroupDisable,
        "/rp_on"=>ChatRpControl.MemberOn,"/rp_off"=>ChatRpControl.MemberOff,_=>null
    };
    public static ChatRpControlEvent? TelegramControl(MessengerRealm realm,string secret,string? header,JsonElement root,DateTimeOffset now)
    {
        if(!realm.Valid||realm.Provider!="telegram"||!MessengerWebhookProof.SecretMatches(secret,header)||
            !TelegramIdentityProof.UniqueObject(root)||!Obj(root,"message",out var m)||
            !Obj(m,"chat",out var chat)||Text(chat,"type") is not ("group" or "supergroup")||
            Num(chat,"id") is not long peer||peer>=0||m.TryGetProperty("message_thread_id",out _)||
            m.TryGetProperty("is_topic_message",out _)||m.TryGetProperty("forward_origin",out _)||
            m.TryGetProperty("via_bot",out _)||TgActor(m) is not { } sender||Id(root,"update_id") is null||
            Date(m,now) is not { } at||Control(Text(m,"text")) is not { } command)return null;
        return new(realm,peer.ToString(CultureInfo.InvariantCulture),sender.Id,at,command);
    }
    public static ChatRpControlEvent? VkControl(MessengerRealm realm,string secret,JsonElement root,DateTimeOffset now)
    {
        if(!MessengerWebhookProof.VkEnvelope(realm,secret,root)||Text(root,"type")!="message_new"||
            !Obj(root,"object",out var obj)||!Obj(obj,"message",out var m)||Num(m,"out")!=0||
            Num(m,"peer_id") is not long peer||peer<2000000001||Id(m,"from_id") is not string sender||
            Date(m,now) is not { } at||!EventId(Text(root,"event_id"))||Control(Text(m,"text")) is not { } command)return null;
        return new(realm,peer.ToString(CultureInfo.InvariantCulture),sender,at,command);
    }

    private static ChatRpDecision? Decision(MessengerRealm realm,string peer,string sender,string? command,string? callback=null)
    {
        if(command is null||command.Length!=37||!(command.StartsWith("rp:a:",StringComparison.Ordinal)||
            command.StartsWith("rp:d:",StringComparison.Ordinal))||!Guid.TryParseExact(command[5..],"N",out var id)||
            id==Guid.Empty||!ChatRpDestination.Valid(realm,peer))return null;
        return new(realm,peer,sender,id,command[3]=='a',callback);
    }

    public static ChatRpDecision? TelegramDecision(MessengerRealm realm,string secret,string? header,JsonElement root,DateTimeOffset now)
    {
        if(!realm.Valid||realm.Provider!="telegram"||!MessengerWebhookProof.SecretMatches(secret,header)||
            !TelegramIdentityProof.UniqueObject(root)||!Obj(root,"callback_query",out var q)||
            !Obj(q,"message",out var m)||!Obj(m,"chat",out var chat)||Text(chat,"type") is not ("group" or "supergroup")||
            m.TryGetProperty("message_thread_id",out _)||m.TryGetProperty("is_topic_message",out _)||
            Num(chat,"id") is not long peer||!Obj(q,"from",out var from)||
            !from.TryGetProperty("is_bot",out var bot)||bot.ValueKind!=JsonValueKind.False||
            Id(from,"id") is not string sender||!Obj(m,"from",out var author)||Id(author,"id")!=realm.BotId||
            Date(m,now) is null||Text(q,"id") is not {Length:>0 and <=128} callback)return null;
        return Decision(realm,peer.ToString(CultureInfo.InvariantCulture),sender,Text(q,"data"),callback);
    }

    // VK inline text buttons emit message_new; the command is scoped to the actual sender and peer.
    public static ChatRpDecision? VkDecision(MessengerRealm realm,string secret,JsonElement root,DateTimeOffset now)
    {
        if(!MessengerWebhookProof.VkEnvelope(realm,secret,root)||Text(root,"type")!="message_new"||
            !Obj(root,"object",out var obj)||!Obj(obj,"message",out var m)||Num(m,"out")!=0||
            Num(m,"peer_id") is not long peer||Id(m,"from_id") is not string sender||
            Date(m,now) is null||!EventId(Text(root,"event_id")))return null;
        return Decision(realm,peer.ToString(CultureInfo.InvariantCulture),sender,Text(m,"text"));
    }

    public static ChatRpEvent? Telegram(MessengerRealm realm,string secret,string? header,JsonElement root,DateTimeOffset now)
    {
        if(!realm.Valid||realm.Provider!="telegram"||!MessengerWebhookProof.SecretMatches(secret,header)||
            !TelegramIdentityProof.UniqueObject(root)||!Obj(root,"message",out var m)||
            !Obj(m,"chat",out var chat)||Text(chat,"type") is not ("group" or "supergroup")||
            Num(chat,"id") is not long peer||peer>=0||m.TryGetProperty("message_thread_id",out _)||
            m.TryGetProperty("is_topic_message",out _)||!Obj(m,"reply_to_message",out var reply)||
            !Obj(reply,"chat",out var targetChat)||Num(targetChat,"id")!=peer||
            m.TryGetProperty("forward_origin",out _)||m.TryGetProperty("via_bot",out _)||
            reply.TryGetProperty("forward_origin",out _)||
            TgActor(m) is not { } actor||TgActor(reply) is not { } target||actor.Id==target.Id||
            Id(root,"update_id") is not string ev||Date(m,now) is not { } at||
            ChatRpCatalog.Parse(Text(m,"text")) is not { } action)return null;
        return new(realm,peer.ToString(CultureInfo.InvariantCulture),ev,at,actor,target,action.Action);
    }

    public static ChatRpEvent? Vk(MessengerRealm realm,string secret,JsonElement root,DateTimeOffset now)
    {
        if(!MessengerWebhookProof.VkEnvelope(realm,secret,root)||Text(root,"type")!="message_new"||
            !Obj(root,"object",out var obj)||!Obj(obj,"message",out var m)||Num(m,"out")!=0||
            Num(m,"peer_id") is not long peer||peer<2000000001||!Obj(m,"reply_message",out var reply)||
            Num(reply,"peer_id")!=peer||Id(m,"from_id") is not string actor||Id(reply,"from_id") is not string target||
            actor==target||!EventId(Text(root,"event_id"))||Date(m,now) is not { } at||
            ChatRpCatalog.Parse(Text(m,"text")) is not { } action)return null;
        // VK display names must be fetched through a trusted profile resolver, never command text.
        return new(realm,peer.ToString(CultureInfo.InvariantCulture),Text(root,"event_id")!,at,
            new(actor,"Отправитель"),new(target,"Собеседник"),action.Action);
    }
}

/// <summary>Only authorized catalog lookup. No arbitrary URL, free-text internet search or personal data.</summary>
public interface IChatRpMediaSource
{
    Task<ChatRpMedia?> Find(string category,CancellationToken ct=default);
}
public sealed record ChatRpMedia(Uri Url,string Attribution);
public sealed record ChatRpApprovedAsset(string Category,Uri Url,string Attribution);
/// <summary>Deployment-owned manifest of licensed/owned anime images and GIFs; HTTPS exact host only.</summary>
public sealed class ApprovedChatRpMedia : IChatRpMediaSource
{
    private readonly ChatRpApprovedAsset[] assets;
    public ApprovedChatRpMedia(string trustedHost,IEnumerable<ChatRpApprovedAsset> approved)
    {
        if(Uri.CheckHostName(trustedHost)!=UriHostNameType.Dns)throw new ArgumentException("Invalid media host.");
        assets=approved.Take(4097).ToArray();
        if(assets.Length>4096||assets.Any(a=>!ChatRpCatalog.All.Any(d=>d.Category==a.Category)||
            !a.Url.IsAbsoluteUri||a.Url.Scheme!="https"||a.Url.Host!=trustedHost||!a.Url.IsDefaultPort||
            a.Url.UserInfo.Length!=0||a.Url.Fragment.Length!=0||a.Url.Query.Length!=0||
            !(a.Url.AbsolutePath.EndsWith(".gif",StringComparison.OrdinalIgnoreCase)||
              a.Url.AbsolutePath.EndsWith(".png",StringComparison.OrdinalIgnoreCase)||
              a.Url.AbsolutePath.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase))||
            string.IsNullOrWhiteSpace(a.Attribution)||a.Attribution.Length>200||a.Attribution.Any(char.IsControl)))
            throw new ArgumentException("Invalid approved RP media manifest.");
    }
    public Task<ChatRpMedia?> Find(string category,CancellationToken ct=default)
    {
        ct.ThrowIfCancellationRequested();
        var options=assets.Where(a=>a.Category==category).ToArray();
        if(options.Length==0)return Task.FromResult<ChatRpMedia?>(null);
        var asset=options[System.Security.Cryptography.RandomNumberGenerator.GetInt32(options.Length)];
        return Task.FromResult<ChatRpMedia?>(new(asset.Url,asset.Attribution));
    }
}

/// <summary>Optional reference renderer. Policy inputs must be derived from trusted current group state.
/// Sensitive actions do not execute unless target consent has been verified for this exact event.
/// No storage, sending or authorization is implied by rendering a card.</summary>
public sealed class ChatRpRenderer(IChatRpMediaSource media)
{
    public async Task<ChatRpCard?> Render(ChatRpEvent e,bool groupEnabled,bool allowedPair,bool targetAccepted,CancellationToken ct=default)
    {
        if(!groupEnabled||!allowedPair||!e.Realm.Valid||e.Actor.Id==e.Target.Id||
            !TelegramIdentityProof.PositiveId(e.Actor.Id)||!TelegramIdentityProof.PositiveId(e.Target.Id)||
            ChatRpCatalog.Find(e.Action) is not { } action)return null;
        var actor=ChatRpCatalog.Name(e.Actor.Name);var target=ChatRpCatalog.Name(e.Target.Name);
        if(action.RequiresConsent&&!targetAccepted)
            return new($"{actor} предлагает действие «{action.Command}» для участника «{target}». Ожидается согласие.",action.Category,true);
        ChatRpMedia? image=null;
        try{image=await media.Find(action.Category,ct);}
        catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidOperationException ||
            ex is OperationCanceledException&&!ct.IsCancellationRequested){}
        return new($"{actor} {action.Verb} «{target}».",action.Category,false,image);
    }
}
