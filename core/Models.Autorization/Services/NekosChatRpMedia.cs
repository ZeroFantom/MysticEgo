using System.Text.Json;

namespace Models.Autorization.Services;

/// <summary>Opt-in, noncommercial reference source only. Operator must verify provider terms and
/// underlying rights first. Returns source URL and attribution, never downloads/rehosts assets.
/// Not registered, no runtime setting enables it automatically.</summary>
public sealed class NekosChatRpMedia : IChatRpMediaSource, IDisposable
{
    private readonly HttpClient http;
    public NekosChatRpMedia(bool nonCommercialTermsAccepted):this(nonCommercialTermsAccepted,
        new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){}
    public NekosChatRpMedia(bool nonCommercialTermsAccepted,HttpMessageHandler handler)
    {
        if(!nonCommercialTermsAccepted)throw new InvalidOperationException("Media terms require explicit approval.");
        http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(5),MaxResponseContentBufferSize=32768};
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MysticEgo-RP/1.0");
    }
    public async Task<ChatRpMedia?> Find(string category,CancellationToken ct=default)
    {
        if(!ChatRpCatalog.All.Any(a=>a.Category==category))return null;
        try
        {
            using var response=await http.GetAsync("https://nekos.best/api/v2/"+category,ct);
            if(!response.IsSuccessStatusCode)return null;
            using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
            var root=json.RootElement;
            if(!TelegramIdentityProof.UniqueObject(root)||!root.TryGetProperty("results",out var results)||
                results.ValueKind!=JsonValueKind.Array||results.GetArrayLength()!=1)return null;
            var item=results[0];
            if(!TelegramIdentityProof.UniqueObject(item)||
                !Uri.TryCreate(TelegramIdentityProof.Text(item,"url"),UriKind.Absolute,out var url)||
                url.Scheme!="https"||url.Host!="nekos.best"||!url.IsDefaultPort||url.UserInfo.Length!=0||
                url.Query.Length!=0||url.Fragment.Length!=0)return null;
            var prefix="/api/v2/"+category+"/";
            if(!url.AbsolutePath.StartsWith(prefix,StringComparison.Ordinal))return null;
            var filename=url.AbsolutePath[prefix.Length..];
            if(!filename.EndsWith(".gif",StringComparison.Ordinal)||
                !Guid.TryParseExact(filename[..^4],"D",out _))return null;
            var anime=TelegramIdentityProof.Text(item,"anime_name");
            if(string.IsNullOrWhiteSpace(anime)||anime.Length>160||anime.Any(char.IsControl))return null;
            return new(url,"Anime: "+anime+" · nekos.best");
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or ArgumentException or InvalidOperationException ||
            ex is OperationCanceledException&&!ct.IsCancellationRequested){return null;}
    }
    public void Dispose()=>http.Dispose();
}
