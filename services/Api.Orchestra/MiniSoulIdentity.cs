using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

/// <summary>Dedicated internal Auth client. Fixed route, no cookies, redirects, URL query or
/// request/response logging in this client. Existing managed identity token source is reused.</summary>
public sealed class MiniSoulIdentity(NodeOptions node,IdentityTokenSource identities):IDisposable
{
    private readonly HttpClient http=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false})
        {Timeout=TimeSpan.FromSeconds(4),MaxResponseContentBufferSize=8192};
    public sealed record Resolution(Guid? UserId,bool Denied);
    public static bool ValidCapability(string? token)=>token is {Length:43}&&
        token.All(c=>char.IsAsciiLetterOrDigit(c)||c is '_' or '-');
    public async Task<Resolution> Resolve(string token,CancellationToken ct)
    {
        if(!ValidCapability(token))return new(null,true);
        if(!Uri.TryCreate(node.AuthUrl,UriKind.Absolute,out var origin)||origin.UserInfo.Length!=0||
            origin.Query.Length!=0||origin.Fragment.Length!=0||origin.AbsolutePath!="/"||
            !(origin.Scheme=="https"||origin.Scheme=="http"&&origin.IsLoopback))return new(null,false);
        try
        {
            var serviceToken=await identities.GetAsync(ct);
            using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(origin,"/api/auth/messenger/internal/mini/resolve"));
            request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",serviceToken);
            request.Headers.Add("X-Mystic-Mini-Session",token);
            using var response=await http.SendAsync(request,ct);
            if(response.StatusCode==HttpStatusCode.Unauthorized)return new(null,true);
            if(!response.IsSuccessStatusCode)return new(null,false);
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var obj=json.RootElement;
            if(obj.ValueKind!=JsonValueKind.Object||obj.EnumerateObject().Count()!=1||
                !obj.TryGetProperty("userId",out var value)||value.ValueKind!=JsonValueKind.String||
                !value.TryGetGuid(out var id)||id==Guid.Empty)return new(null,false);
            return new(id,false);
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or ArgumentException or InvalidOperationException||
            ex is OperationCanceledException&&!ct.IsCancellationRequested){return new(null,false);}
    }
    public void Dispose()=>http.Dispose();
}
