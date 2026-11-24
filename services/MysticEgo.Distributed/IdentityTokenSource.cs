using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
namespace MysticEgo.Distributed;
public sealed class IdentityTokenSource
{
    private readonly NodeOptions _options;
    private readonly IHttpClientFactory _http;
    private readonly ILogger<IdentityTokenSource> _log;
    private string? _token;
    private DateTimeOffset _validUntil;
    private readonly SemaphoreSlim _lock=new(1,1);
    public IdentityTokenSource(NodeOptions options,IHttpClientFactory http,ILogger<IdentityTokenSource> log){_options=options;_http=http;_log=log;}
    public async Task<string> GetAsync(CancellationToken ct=default)
    {
        if(_token is not null && _validUntil>DateTimeOffset.UtcNow.AddSeconds(45))return _token;
        await _lock.WaitAsync(ct);
        try
        {
            if(_token is not null && _validUntil>DateTimeOffset.UtcNow.AddSeconds(45))return _token;
            if(!Guid.TryParse(_options.IdentityId,out var id)||string.IsNullOrWhiteSpace(_options.IdentitySecret))throw new InvalidOperationException("Идентичность узла не зарегистрирована (Node:IdentityId + Node:IdentitySecret)");
            var baseUrl=_options.AuthUrl.TrimEnd('/');
            if(!baseUrl.StartsWith("https://",StringComparison.OrdinalIgnoreCase)&&!Uri.TryCreate(baseUrl,UriKind.Absolute,out var uri))throw new InvalidOperationException("Некорректный адрес сервера авторизации (AuthUrl)");
            if(baseUrl.StartsWith("http://") && !new Uri(baseUrl).IsLoopback && !_options.AllowInsecureTestHttp)throw new InvalidOperationException("Для доступа к серверу авторизации вне текущей машины требуется HTTPS");
            using var client=_http.CreateClient();client.Timeout=TimeSpan.FromSeconds(8);
            using var response=await client.PostAsJsonAsync(baseUrl+"/api/identities/token",new {IdentityId=id,Secret=_options.IdentitySecret},ct);
            response.EnsureSuccessStatusCode();
            var issued=await response.Content.ReadFromJsonAsync<IssuedToken>(cancellationToken:ct)??throw new InvalidOperationException("Сервер авторизации вернул некорректный токен");
            _token=issued.AccessToken;_validUntil=issued.ExpiresAt;
            return _token;
        }
        finally {_lock.Release();}
    }
    private sealed record IssuedToken(string AccessToken,DateTimeOffset ExpiresAt);
}
