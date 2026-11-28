using System.Globalization;
using System.Text.Json;

namespace Models.Autorization.Services;

public enum ChatRpMemberRole { Unknown, Member, Admin }

/// <summary>Live provider membership lookup. Fail closed, fixed endpoint, no redirects or logging.</summary>
public sealed class ChatRpMembership : IDisposable
{
    private readonly HttpClient http;
    public ChatRpMembership():this(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
    public ChatRpMembership(HttpMessageHandler handler)=>http=new(handler)
        {Timeout=TimeSpan.FromSeconds(5),MaxResponseContentBufferSize=262144};
    public async Task<ChatRpMemberRole> Role(MessengerRealm realm,string peer,string subject,string? token,CancellationToken ct=default)
    {
        if(!ChatRpDestination.Valid(realm,peer)||!TelegramIdentityProof.PositiveId(subject)||
            token is not {Length:>=16 and <=512})return ChatRpMemberRole.Unknown;
        string endpoint;var fields=new Dictionary<string,string>();
        if(realm.Provider=="telegram")
        {
            var prefix=realm.BotId+":";
            if(!token.StartsWith(prefix,StringComparison.Ordinal)||token.Length-prefix.Length<16||
                !token[prefix.Length..].All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-'))return ChatRpMemberRole.Unknown;
            endpoint="https://api.telegram.org/bot"+token+"/getChatMember";
            fields["chat_id"]=peer;fields["user_id"]=subject;
        }
        else
        {
            if(!token.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-' or '.'))return ChatRpMemberRole.Unknown;
            endpoint="https://api.vk.com/method/messages.getConversationMembers";
            fields["access_token"]=token;fields["peer_id"]=peer;fields["v"]="5.199";
        }
        try
        {
            using var response=await http.PostAsync(endpoint,new FormUrlEncodedContent(fields),ct);
            if(!response.IsSuccessStatusCode)return ChatRpMemberRole.Unknown;
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root=json.RootElement;
            if(!TelegramIdentityProof.UniqueObject(root)||root.TryGetProperty("error",out _))return ChatRpMemberRole.Unknown;
            if(realm.Provider=="telegram")
            {
                if(!root.TryGetProperty("ok",out var ok)||ok.ValueKind!=JsonValueKind.True||
                    !root.TryGetProperty("result",out var result)||!TelegramIdentityProof.UniqueObject(result)||
                    !result.TryGetProperty("user",out var user)||!TelegramIdentityProof.UniqueObject(user)||
                    !user.TryGetProperty("id",out var id)||!id.TryGetInt64(out var uid)||
                    uid.ToString(CultureInfo.InvariantCulture)!=subject)return ChatRpMemberRole.Unknown;
                return TelegramIdentityProof.Text(result,"status") switch
                {
                    "creator" or "administrator"=>ChatRpMemberRole.Admin,
                    "member"=>ChatRpMemberRole.Member,
                    _=>ChatRpMemberRole.Unknown
                };
            }
            if(!root.TryGetProperty("response",out var body)||!TelegramIdentityProof.UniqueObject(body)||
                !body.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array||
                items.GetArrayLength()>1000)return ChatRpMemberRole.Unknown;
            ChatRpMemberRole? found=null;
            foreach(var item in items.EnumerateArray())
            {
                if(!TelegramIdentityProof.UniqueObject(item)||!item.TryGetProperty("member_id",out var id)||
                    id.ValueKind!=JsonValueKind.Number||!id.TryGetInt64(out var uid))return ChatRpMemberRole.Unknown;
                if(uid.ToString(CultureInfo.InvariantCulture)!=subject)continue;
                if(found is not null)return ChatRpMemberRole.Unknown;
                found=(item.TryGetProperty("is_admin",out var admin)&&admin.ValueKind==JsonValueKind.True)||
                    (item.TryGetProperty("is_owner",out var owner)&&owner.ValueKind==JsonValueKind.True)
                    ?ChatRpMemberRole.Admin:ChatRpMemberRole.Member;
            }
            return found??ChatRpMemberRole.Unknown;
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidOperationException or ArgumentException||
            ex is OperationCanceledException&&!ct.IsCancellationRequested){return ChatRpMemberRole.Unknown;}
    }
    public void Dispose()=>http.Dispose();
}
