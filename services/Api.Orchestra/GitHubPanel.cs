using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

public sealed class GitHubPanel(IHttpClientFactory clients, IConfiguration config)
{
    public const string Repository = "ZeroFantom/MysticEgo";
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(DateTimeOffset Until,JsonArray Data)> cache=new();

    public async Task<JsonArray> List(string kind,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if(cache.TryGetValue(kind,out var saved)&&saved.Until>DateTimeOffset.UtcNow)return saved.Data;
            using var client=clients.CreateClient("panel-github");
            using var request=new HttpRequestMessage(HttpMethod.Get,$"https://api.github.com/repos/{Repository}/{kind}?per_page=100");
            if(config["PanelGitHub:Token"] is {Length:>0} token)
                request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
            using var response=await client.SendAsync(request,ct);
            response.EnsureSuccessStatusCode();
            var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonArray
                ?? throw new InvalidDataException("Invalid GitHub response");
            cache[kind]=(DateTimeOffset.UtcNow.AddMinutes(2),data);
            return data;
        }
        finally{gate.Release();}
    }

    public async Task<object?> Latest(CancellationToken ct)
    {
        var recent=(await List("releases",ct)).OfType<JsonObject>()
            .Where(r=>r["draft"]?.GetValue<bool>()==false&&r["published_at"]!=null)
            .OrderByDescending(r=>DateTimeOffset.TryParse(r["published_at"]?.ToString(),out var date)?date:DateTimeOffset.MinValue)
            .FirstOrDefault();
        return recent is null?null:new {tag=recent["tag_name"]?.ToString(),publishedAt=recent["published_at"]?.ToString(),
            url=recent["html_url"]?.ToString(),prerelease=recent["prerelease"]?.GetValue<bool>()==true};
    }

    public async Task<object> Branches(CancellationToken ct)
    {
        var data=await List("branches",ct);
        return new {truncated=data.Count==100,branches=data.OfType<JsonObject>().Select(b=>new
        {
            name=b["name"]?.ToString(), sha=b["commit"]?["sha"]?.ToString(), isProtected=b["protected"]?.GetValue<bool>()==true
        }).ToArray()};
    }
}

[ApiController]
public sealed class GitHubPanelController(GitHubPanel github,DevelopmentTeam team) : ControllerBase
{
    [AllowAnonymous,HttpGet("api/v1/public/project-release")]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        Response.Headers.AccessControlAllowOrigin="*";
        try{return Ok(new {latest=await github.Latest(ct)});}
        catch(Exception e) when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException || e is TaskCanceledException&&!ct.IsCancellationRequested)
        {return StatusCode(503,new {error="GitHub недоступен."});}
    }

    [HttpGet("api/v1/managed/project-branches"),Authorize(Policy="operator")]
    public async Task<IActionResult> Branches(CancellationToken ct)
    {
        if(!team.IsOwner(User)&&!await team.IsMember(User,ct))return Forbid();
        try{return Ok(await github.Branches(ct));}
        catch(Exception e) when(e is HttpRequestException or InvalidDataException or System.Text.Json.JsonException || e is TaskCanceledException&&!ct.IsCancellationRequested)
        {return StatusCode(503,new {error="Не удалось получить ветки GitHub."});}
    }
}
