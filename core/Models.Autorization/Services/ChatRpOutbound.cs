using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Models.Autorization.Domain.Entities;

namespace Models.Autorization.Services;

/// <summary>One attempt for a claimed group RP card. Only approved native asset identifiers
/// or deployment-owned HTTPS media; no arbitrary provider URLs, user uploads or rehosting.
/// Dedicated handler must not follow redirects or log Telegram credentials.</summary>
public sealed class ChatRpOutbound : IDisposable
{
    private readonly HttpClient http;
    public ChatRpOutbound():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
    public ChatRpOutbound(HttpMessageHandler handler)=>
        http=new(handler){Timeout=TimeSpan.FromSeconds(8),MaxResponseContentBufferSize=65536};

    public async Task<bool> Send(ChatRpDelivery row,string? token,ChatRpMedia? media,string? vkAttachment,CancellationToken ct=default)
    {
        var realm=new MessengerRealm(row.Provider,row.BotId);
        if(!ChatRpDestination.Valid(realm,row.Peer)||row.Caption is not {Length:>0 and <=512}||
            !ChatRpCatalog.All.Any(x=>x.Category==row.Category&&!x.RequiresConsent)||
            token is not {Length:>=16 and <=512})return false;
        var fields=new Dictionary<string,string>();
        string endpoint;
        if(realm.Provider=="telegram")
        {
            var prefix=realm.BotId+":";
            if(!token.StartsWith(prefix,StringComparison.Ordinal)||token.Length-prefix.Length<16||
                !token[prefix.Length..].All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-'))return false;
            fields["chat_id"]=row.Peer;
            var method="sendMessage";
            if(media is not null&&media.Url.IsAbsoluteUri&&media.Url.Scheme=="https"&&media.Url.IsDefaultPort&&
                media.Url.UserInfo.Length==0&&media.Url.Query.Length==0&&media.Url.Fragment.Length==0&&
                media.Url.HostNameType==UriHostNameType.Dns&&media.Attribution.Length<=200&&!media.Attribution.Any(char.IsControl))
            {
                var path=media.Url.AbsolutePath;
                var gif=path.EndsWith(".gif",StringComparison.OrdinalIgnoreCase);
                if(gif||path.EndsWith(".jpg",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".png",StringComparison.OrdinalIgnoreCase))
                {
                    method=gif?"sendAnimation":"sendPhoto";
                    fields[gif?"animation":"photo"]=media.Url.AbsoluteUri;
                    fields["caption"]=row.Caption+"\n"+media.Attribution;
                }
            }
            if(method=="sendMessage"){fields["text"]=row.Caption;fields["disable_web_page_preview"]="true";}
            endpoint="https://api.telegram.org/bot"+token+"/"+method;
        }
        else
        {
            if(!token.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-' or '.'))return false;
            endpoint="https://api.vk.com/method/messages.send";
            fields["access_token"]=token;fields["peer_id"]=row.Peer;fields["message"]=row.Caption;fields["v"]="5.199";
            var digest=SHA256.HashData(Encoding.UTF8.GetBytes("group-rp:"+row.Id.ToString("D")));
            var random=(int)(System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(digest)&0x7fffffff);
            fields["random_id"]=(random==0?1:random).ToString(CultureInfo.InvariantCulture);
            // VK requires an already uploaded, operator-approved native attachment.
            if(vkAttachment is {Length:>0 and <=80}&&System.Text.RegularExpressions.Regex.IsMatch(vkAttachment,
                @"\A(?:photo|doc)-?[1-9][0-9]{0,18}_[1-9][0-9]{0,18}\z"))
                fields["attachment"]=vkAttachment;
        }
        try
        {
            using var request=new HttpRequestMessage(HttpMethod.Post,endpoint){Content=new FormUrlEncodedContent(fields)};
            using var response=await http.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)return false;
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var body=json.RootElement;
            if(!TelegramIdentityProof.UniqueObject(body)||body.TryGetProperty("error",out _))return false;
            return realm.Provider=="telegram"?body.TryGetProperty("ok",out var ok)&&ok.ValueKind==JsonValueKind.True:
                body.TryGetProperty("response",out var value)&&value.ValueKind==JsonValueKind.Number&&value.TryGetInt64(out var n)&&n>0;
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or ArgumentException or InvalidOperationException||
            ex is OperationCanceledException&&!ct.IsCancellationRequested){return false;}
    }

    public void Dispose()=>http.Dispose();
}
