using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace MysticEgo.Api.Orchestra;

/// <summary>Fixed-origin Timeweb API transport. Never accepts user-controlled URLs or exposes the token.</summary>
public sealed class TimewebApiClient(IHttpClientFactory clients,IConfiguration config)
{
    public async Task<JsonObject?> Get(string route,CancellationToken ct)
    {
        using var result=await Send(HttpMethod.Get,route,ct);
        result.EnsureSuccessStatusCode();
        return JsonNode.Parse(await result.Content.ReadAsStringAsync(ct)) as JsonObject;
    }
    public async Task<HttpResponseMessage> Send(HttpMethod method,string route,CancellationToken ct)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(route,@"\Aservers/[1-9][0-9]*(?:/(?:ips|start|shutdown|reboot))?\z"))
            throw new ArgumentException("Unsupported Timeweb server operation");
        var token=config["Timeweb:Token"];
        if(string.IsNullOrWhiteSpace(token))throw new InvalidOperationException("Timeweb token not configured");
        using var client=clients.CreateClient("mysticego-timeweb");
        using var request=new HttpRequestMessage(method,"https://api.timeweb.cloud/api/v1/"+route);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
        // Buffer within the HttpClient's configured timeout before disposing the client.
        return await client.SendAsync(request,ct);
    }
    public static string Value(JsonObject item,string key)=>item[key] is JsonValue value?value.ToString():"";
}
