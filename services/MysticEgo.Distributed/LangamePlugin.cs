using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

/// <summary>Опциональный адаптер старого Langame api_front/auth.php.
/// Токены существуют только в памяти этого процесса, на фронтенд не передаются.
/// Запуск нескольких экземпляров требует отдельного безопасного хранилища сессий.</summary>
public sealed class LangameSessionCache
{
    public ConcurrentDictionary<string, LangameSessionInfo> Sessions {get;}=new();
}
public sealed record LangameSessionInfo(string Login,string AccessToken,string? MasterToken,string? ClubId,DateTimeOffset ExpiresAt);
public sealed record LangameLoginInput(string Login,string Password);

public sealed record LangameStatus(bool Enabled,bool Connected,string? Login,string? ClubId);
public interface ILangameService
{
    LangameStatus Status(string user);
    Task<LangameStatus> Login(string user,LangameLoginInput input,CancellationToken ct);
    LangameStatus Logout(string user);
}
public sealed class LangameService(IConfiguration configuration,IHttpClientFactory factory,LangameSessionCache cache) : ILangameService
{
    private Uri? BaseUrl()
    {
        if(configuration.GetValue<bool>("Langame:Enabled")!=true)return null;
        if(!Uri.TryCreate(configuration["Langame:BaseUrl"],UriKind.Absolute,out var uri))return null;
        return uri.Scheme==Uri.UriSchemeHttps||(uri.Scheme==Uri.UriSchemeHttp&&uri.IsLoopback)?uri:null;
    }
    public LangameStatus Status(string user)
    {
        var connected=cache.Sessions.TryGetValue(user,out var session)&&session.ExpiresAt>DateTimeOffset.UtcNow;
        return new(BaseUrl()!=null,connected,connected?session?.Login:null,connected?session?.ClubId:null);
    }
    public async Task<LangameStatus> Login(string user,LangameLoginInput input,CancellationToken ct)
    {
        var uri=BaseUrl()??throw new NodeApiException(400,"Плагин Langame выключен или URL небезопасен.");
        if(input.Login.Length is <1 or >128||input.Password.Length is <1 or >512)throw new NodeApiException(400,"Некорректные данные входа.");
        using var client=factory.CreateClient();client.Timeout=TimeSpan.FromSeconds(15);
        using var form=new FormUrlEncodedContent(new Dictionary<string,string>{{"action","login"},{"login",input.Login},{"password",input.Password}});
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(uri,"api_front/auth.php?lang=ru")){Content=form};
        request.Headers.Referrer=new Uri(uri,"admins/");
        try
        {
            using var response=await client.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)throw new NodeApiException(502,"Langame HTTP "+(int)response.StatusCode);
            var body=await response.Content.ReadAsStringAsync(ct);
            using var doc=JsonDocument.Parse(body);
            if(!doc.RootElement.TryGetProperty("status",out var status)||status.ValueKind!=JsonValueKind.True||!doc.RootElement.TryGetProperty("data",out var data))throw new NodeApiException(401,"Неверные учётные данные Langame.");
            static string? Get(JsonElement e,string field)=>e.TryGetProperty(field,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString():null;
            var token=Get(data,"token");
            if(string.IsNullOrWhiteSpace(token))throw new NodeApiException(502,"Langame не вернул токен.");
            var club=data.TryGetProperty("club_id",out var id)?id.ToString():null;
            cache.Sessions[user]=new LangameSessionInfo(input.Login,token,Get(data,"token_master_api"),club,DateTimeOffset.UtcNow.AddHours(12));
            return new(true,true,input.Login,club);
        }
        catch(HttpRequestException){throw new NodeApiException(503,"Langame недоступен.");}
        catch(JsonException){throw new NodeApiException(502,"Ответ Langame не является JSON.");}
    }
    public LangameStatus Logout(string user){cache.Sessions.TryRemove(user,out _);return Status(user);}
}
