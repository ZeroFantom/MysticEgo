using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Models.Autorization.Services;

/// <summary>Best-effort operational replies only. No NPC text or provider credentials in results.
/// Dedicated client with redirects disabled, bounded response and no IHttpClientFactory URL logger.
/// Deployments must also suppress external HTTP tracing of Telegram's token-bearing URI.</summary>
public sealed class MessengerOutbound : IDisposable
{
    private readonly HttpClient http;
    public MessengerOutbound():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
    public MessengerOutbound(HttpMessageHandler handler) =>
        http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=65536};

    public static string? ReplyText(MessengerIngressStatus status) => status switch
    {
        MessengerIngressStatus.Help => "Mystic Ego: откройте настройки безопасности аккаунта, начните привязку чат-бота и отправьте полученный код командой /link. /status — проверить привязку. Никогда не отправляйте пароль.",
        MessengerIngressStatus.LinkStaged => "Запрос получен. Проверьте идентификатор в Mystic Ego и подтвердите привязку там.",
        MessengerIngressStatus.LinkUnavailable => "Запрос недоступен. Начните новую привязку в настройках Mystic Ego.",
        MessengerIngressStatus.Linked => "Чат-бот привязан. Игровая связь пока недоступна.",
        MessengerIngressStatus.Unlinked => "Чат-бот не привязан. Начните привязку в настройках Mystic Ego.",
        _=>null
    };

    public async Task<bool> Reply(MessengerRealm realm,PrivateMessengerEvent message,MessengerIngressStatus status,
        string? token,CancellationToken ct=default)
    {
        var text=ReplyText(status);
        if(text is null||!realm.Valid||!message.Sender.PrivateHumanChat||
            message.Sender.Provider!=realm.Provider||message.Sender.BotId!=realm.BotId||
            !TelegramIdentityProof.PositiveId(message.Sender.UserId)||!ValidToken(realm,token))
            return false;
        try
        {
            // Never echo input, link challenges, provider subjects or account details.
            var fields=new Dictionary<string,string>();
            string endpoint;
            if(realm.Provider=="telegram")
            {
                endpoint="https://api.telegram.org/bot"+token+"/sendMessage";
                fields["chat_id"]=message.Sender.UserId;
                fields["text"]=text;
            }
            else
            {
                endpoint="https://api.vk.com/method/messages.send";
                fields["access_token"]=token!;
                fields["peer_id"]=message.Sender.UserId;
                fields["message"]=text;
                fields["random_id"]=RandomId(realm,message.EventId).ToString(CultureInfo.InvariantCulture);
                fields["v"]="5.199";
            }
            using var request=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new FormUrlEncodedContent(fields)};
            using var response=await http.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)return false;
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var body=json.RootElement;
            if(!TelegramIdentityProof.UniqueObject(body)||body.TryGetProperty("error",out _))return false;
            if(realm.Provider=="telegram")return body.TryGetProperty("ok",out var ok)&&ok.ValueKind==JsonValueKind.True;
            return body.TryGetProperty("response",out var result)&&result.ValueKind==JsonValueKind.Number&&
                result.TryGetInt64(out var id)&&id>0;
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidOperationException or ArgumentException ||
            ex is OperationCanceledException&&!ct.IsCancellationRequested)
        {return false;}
    }

    private static bool ValidToken(MessengerRealm realm,string? token)
    {
        if(token is not {Length:>=16 and <=512})return false;
        if(realm.Provider=="vk")return token.All(c=>c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.');
        var split=token.IndexOf(':');
        return split>0&&token[..split]==realm.BotId&&token[(split+1)..].Length>=16&&
            token[(split+1)..].All(c=>c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    }

    private static int RandomId(MessengerRealm realm,string id)
    {
        var hash=SHA256.HashData(Encoding.UTF8.GetBytes(realm.Provider+"\n"+realm.BotId+"\n"+id));
        return (int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(hash)&0x7fffffff) is var value&&value!=0?value:1;
    }
    public void Dispose()=>http.Dispose();
}
