using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MysticEgo.Api.Orchestra.Development;

/// <summary>Fixed-repository transport for the developer workspace. Separate write credentials from the release reader.</summary>
public sealed class GitHubWorkspaceClient(IHttpClientFactory clients,IConfiguration config,GitHubPanel panel)
{
    public bool CanWrite=>!string.IsNullOrWhiteSpace(config["DevelopmentWorkspace:WriteToken"]);
    public bool CanRead=>CanWrite||panel.HasCredential;
    public const string Repository=GitHubPanel.Repository;

    /// <summary>Only application-owned routes and GraphQL documents may reach GitHub.</summary>
    public async Task<JsonNode> Rest(HttpMethod method,string route,object? body,CancellationToken ct)
    {
        if(!Regex.IsMatch(route,@"\Aissues(?:/[1-9][0-9]*(?:/(?:comments|assignees))?)?(?:\?state=all&sort=updated&direction=desc&per_page=50&page=[1-9][0-9]*|\?per_page=50&page=[1-9][0-9]*)?\z"))
            throw new ArgumentException("Unsupported workspace route");
        return await Send(method,"https://api.github.com/repos/"+Repository+"/"+route,body,method!=HttpMethod.Get,ct);
    }

    public async Task<JsonNode> Graph(string query,object variables,bool mutation,CancellationToken ct)
    {
        var reply=await Send(HttpMethod.Post,"https://api.github.com/graphql",new{query,variables},mutation,ct);
        if(reply["errors"] is JsonArray {Count:>0}||reply["data"] is not JsonObject data)
            throw new WorkspaceProviderException(502,"github_graphql_error");
        return data;
    }

    private async Task<JsonNode> Send(HttpMethod method,string url,object? body,bool write,CancellationToken ct)
    {
        var token=write?config["DevelopmentWorkspace:WriteToken"]:config["DevelopmentWorkspace:WriteToken"]??panel.ReadCredential();
        if(string.IsNullOrWhiteSpace(token))throw new WorkspaceProviderException(503,write?"github_write_disabled":"github_not_connected");
        using var client=clients.CreateClient("workspace-github");
        using var request=new HttpRequestMessage(method,url);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token.Trim());
        if(body is not null)request.Content=JsonContent.Create(body);
        try
        {
            using var response=await client.SendAsync(request,ct);
            if(!response.IsSuccessStatusCode)
            {
                var code=(int)response.StatusCode;
                throw new WorkspaceProviderException(code is 401 or 403 or 429?503:code==404?404:code==422?409:502,
                    code is 403 or 429?"github_permission_or_rate_limit":code==422?"github_validation_failed":"github_request_failed");
            }
            return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))??throw new WorkspaceProviderException(502,"github_invalid_response");
        }
        catch(Exception e) when(e is HttpRequestException or System.Text.Json.JsonException || e is OperationCanceledException&&!ct.IsCancellationRequested)
        {throw new WorkspaceProviderException(502,write?"github_write_outcome_unknown":"github_unavailable");}
    }
}

public sealed class WorkspaceProviderException(int status,string code):Exception(code)
{
    public int Status{get;}=status;
    public string Code{get;}=code;
}
