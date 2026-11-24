using System.Net;
using System.Text.Json.Nodes;
sealed partial class CloudDesk {
    static readonly HttpClient publicDnsHttp=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(8)};
    async Task<(int status,string[] addresses)> PublicDnsQuery(string endpoint,string name,string type){
        using var request=new HttpRequestMessage(HttpMethod.Get,endpoint+"?name="+Uri.EscapeDataString(name)+"&type="+type);
        request.Headers.Accept.ParseAdd("application/dns-json");
        using var response=await publicDnsHttp.SendAsync(request);response.EnsureSuccessStatusCode();
        var payload=JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        if(payload["Status"] is not JsonValue status||!status.TryGetValue<int>(out var code))throw new Exception("Некорректный DNS-ответ.");
        var rr=type=="A"?"1":"28";
        return (code,(payload["Answer"] as JsonArray??new()).OfType<JsonObject>().Where(r=>S(r,"type")==rr).Select(r=>S(r,"data")).Where(v=>IPAddress.TryParse(v,out _)).Distinct().ToArray());
    }
    async Task<(string? error,string detail)> PublicDnsReady(string domain,string host,JsonArray ips,bool dual){
        var details=new List<string>();bool confirmed=false,conflict=false;
        foreach(var endpoint in new[]{"https://cloudflare-dns.com/dns-query","https://dns.google/resolve"}){
            var provider=new Uri(endpoint).Host;
            try{
                var a=await PublicDnsQuery(endpoint,domain,"A");var aaaa=await PublicDnsQuery(endpoint,domain,"AAAA");
                details.Add($"{provider}: A [{string.Join(", ",a.addresses)}] (код {a.status}); AAAA [{string.Join(", ",aaaa.addresses)}] (код {aaaa.status})");
                if(a.addresses.Any(ip=>!SameIp(ip,host))||aaaa.addresses.Any(ip=>!ips.OfType<JsonObject>().Any(x=>S(x,"type")=="ipv6"&&SameIp(S(x,"ip"),ip))))conflict=true;
                if(a.status==0&&aaaa.status==0&&a.addresses.Length>0&&a.addresses.All(ip=>SameIp(ip,host))&&(!dual||aaaa.addresses.Length>0))confirmed=true;
            }catch(Exception){details.Add(provider+": проверка недоступна");}
        }
        return (conflict?"Публичный DNS содержит конфликтующий адрес.":!confirmed?"Записи ещё не подтверждены публичным DNS. Ожидаем автоматически.":null,string.Join(" · ",details));
    }
}
