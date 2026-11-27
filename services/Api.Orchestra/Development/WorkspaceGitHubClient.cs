using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace MysticEgo.Api.Orchestra.Development;

/// <summary>Repository-scoped GitHub transport. Write credentials are separate from release credentials.</summary>
public sealed class WorkspaceGitHubClient(IHttpClientFactory clients,IConfiguration config)
{
    public bool Configured => !string.IsNullOrWhiteSpace(config["DevelopmentGitHub:Token"]);
    public bool WritesEnabled => Configured && config.GetValue("DevelopmentGitHub:AllowWrites",false);
    public string? LinkedLogin(Guid actor) => config["DevelopmentGitHub:Members:"+actor.ToString("D")];
    public async Task<JsonNode> Rest(HttpMethod method,string route,object? body,CancellationToken ct)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(route,@"\Aissues(?:\?state=all&per_page=100&page=[1-9][0-9]*|/[1-9][0-9]*(?:/comments(?:\?per_page=100&page=[1-3])?)?)?\z"))
            throw new ArgumentException("Unsupported GitHub route");
        return await Send(method,"https://api.github.com/repos/"+GitHubPanel.Repository+"/"+route,body,ct);
    }
    public async Task<JsonObject> Graph(string query,object variables,bool mutation,CancellationToken ct)
    {
        if(mutation&&!WritesEnabled)throw new InvalidOperationException("GitHub writes disabled");
        var result=await Send(HttpMethod.Post,"https://api.github.com/graphql",new{query,variables},ct,mutation);
        if(result["errors"] is JsonArray errors&&errors.Count>0)throw new HttpRequestException("GitHub GraphQL operation failed");
        return result["data"] as JsonObject??throw new HttpRequestException("Missing GraphQL data");
    }
    private async Task<JsonNode> Send(HttpMethod method,string url,object? body,CancellationToken ct,bool? mutation=null)
    {
        if(!Configured)throw new InvalidOperationException("GitHub workspace token not configured");
        if((mutation??(method!=HttpMethod.Get))&&!WritesEnabled)throw new InvalidOperationException("GitHub writes disabled");
        using var client=clients.CreateClient("development-github");
        using var request=new HttpRequestMessage(method,url);
        request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",config["DevelopmentGitHub:Token"]);
        if(body is not null)request.Content=JsonContent.Create(body);
        using var response=await client.SendAsync(request,ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))??throw new HttpRequestException("Empty GitHub response");
    }
}
