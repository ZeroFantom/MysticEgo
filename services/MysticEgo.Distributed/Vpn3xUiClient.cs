using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace MysticEgo.Distributed;

/// <summary>Restricted 3x-ui v3 adapter for deleting an already provisioned client.
/// Tokens are supplied via server configuration Vpn:PanelTokens:{panelGuid}; never via HTTP request.</summary>
public static class Vpn3xUiClient
{
    public sealed record RevocationResult(bool Success,string Error);
    public static bool IsValidClientName(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Length<=128 &&
        System.Text.RegularExpressions.Regex.IsMatch(email,@"^[a-zA-Z0-9][a-zA-Z0-9_.@+-]*$");

    public static async Task<RevocationResult> VerifyClientAsync(VpnPanel panel,string email,IConfiguration config,CancellationToken ct)
        => await SendClientRequestAsync(panel,email,config,ct,delete:false);

    public static async Task<RevocationResult> DeleteClientAsync(VpnPanel panel,
        string email,IConfiguration config,CancellationToken ct)
        => await SendClientRequestAsync(panel,email,config,ct,delete:true);

    private static async Task<RevocationResult> SendClientRequestAsync(VpnPanel panel,string email,IConfiguration config,CancellationToken ct,bool delete)
    {
        if(!IsValidClientName(email))return new(false,"invalid client identifier");
        if(!Uri.TryCreate(panel.BaseUrl,UriKind.Absolute,out var origin) ||
           origin.Scheme!=Uri.UriSchemeHttps || origin.UserInfo.Length!=0 || origin.Fragment.Length!=0)
            return new(false,"invalid HTTPS panel endpoint");
        // Exact, operator-approved panel URL; all redirects disabled to avoid bearer credential forwarding.
        var token=config[$"Vpn:PanelTokens:{panel.Id:D}"];
        if(string.IsNullOrWhiteSpace(token))return new(false,"server-side panel token is missing");
        try
        {
            using var http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false})
                {Timeout=TimeSpan.FromSeconds(10)};
            var address=new Uri(origin.ToString().TrimEnd('/')+(delete?"/panel/api/clients/del/":"/panel/api/clients/get/")+Uri.EscapeDataString(email));
            using var req=new HttpRequestMessage(delete?HttpMethod.Post:HttpMethod.Get,address);
            req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using var response=await http.SendAsync(req,HttpCompletionOption.ResponseHeadersRead,ct);
            if(!response.IsSuccessStatusCode)return new(false,$"3x-ui HTTP {(int)response.StatusCode}");
            await using var stream=await response.Content.ReadAsStreamAsync(ct);
            using var json=await JsonDocument.ParseAsync(stream,cancellationToken:ct);
            if(!json.RootElement.TryGetProperty("success",out var success) || success.ValueKind!=JsonValueKind.True)
                return new(false,delete?"3x-ui did not confirm deletion":"3x-ui client not verified");
            return new(true,"");
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return new(false,"3x-ui timeout");}
        catch(Exception ex) when(ex is HttpRequestException or JsonException or UriFormatException)
        {return new(false,"3x-ui unavailable or invalid response");}
    }
}
