using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;
using System.Net;

namespace MysticEgo.Api.Orchestra;

public sealed class GitHubPanel(IHttpClientFactory clients, IConfiguration config, NodeState state)
{
    public const string Repository = "ZeroFantom/MysticEgo";
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,(DateTimeOffset Until,JsonArray Data)> cache=new();

    private string TokenFile => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(state.Options.DataPath))!, "github-read-token");
    private string? EnvironmentToken => config["PanelGitHub:Token"];

    private string? Token() => File.Exists(TokenFile) ? File.ReadAllText(TokenFile).Trim() : EnvironmentToken;

    public object Connection() => new {
        connected = !string.IsNullOrWhiteSpace(Token()),
        source = File.Exists(TokenFile) ? "panel" : string.IsNullOrWhiteSpace(EnvironmentToken) ? "none" : "environment"
    };

    public async Task Connect(string token, CancellationToken ct)
    {
        token = token.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(token, @"^[A-Za-z0-9_]{20,300}$"))
            throw new ArgumentException("Укажите действующий токен GitHub с доступом Contents: Read.");
        await gate.WaitAsync(ct);
        try
        {
            await Fetch("branches", token, ct);
            await Fetch("releases", token, ct);
            var temporary = TokenFile + "." + Guid.NewGuid().ToString("N");
            try
            {
                var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows())
                    options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                await using (var writer = new StreamWriter(new FileStream(temporary, options)))
                    await writer.WriteAsync(token.AsMemory(), ct);
                File.Move(temporary, TokenFile, overwrite:true);
                cache.Clear();
            }
            finally { File.Delete(temporary); }
        }
        finally { gate.Release(); }
    }

    public async Task Disconnect(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { File.Delete(TokenFile); cache.Clear(); }
        finally { gate.Release(); }
    }

    private async Task<JsonArray> Fetch(string kind, string? token, CancellationToken ct)
    {
        using var client = clients.CreateClient("panel-github");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/{kind}?per_page=100");
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonArray
            ?? throw new InvalidDataException("Unexpected GitHub response");
    }

    public async Task<JsonArray> List(string kind, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue(kind,out var saved) && saved.Until > DateTimeOffset.UtcNow) return saved.Data;
            var data = await Fetch(kind, Token(), ct);
            cache[kind] = (DateTimeOffset.UtcNow.AddMinutes(2), data);
            return data;
        }
        finally { gate.Release(); }
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
    [HttpGet("api/v1/managed/project-github"), Authorize(Policy="operator")]
    [ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
    public IActionResult Connection()
    {
        if (!team.IsOwner(User)) return Forbid();
        try { return Ok(github.Connection()); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        { return StatusCode(503, new {error="Orchestra не может прочитать настройки GitHub."}); }
    }

    [HttpPost("api/v1/managed/project-github"), Authorize(Policy="operator"), RequestSizeLimit(4096)]
    public async Task<IActionResult> Connect(ConnectInput input, CancellationToken ct)
    {
        if (!team.IsOwner(User)) return Forbid();
        try { await github.Connect(input.Token,ct); return Ok(github.Connection()); }
        catch (ArgumentException e) { return BadRequest(new {error=e.Message}); }
        catch (HttpRequestException e) when (e.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        { return BadRequest(new {error="Токен не даёт доступ к веткам и релизам ZeroFantom/MysticEgo. Проверьте Contents: Read."}); }
        catch (Exception e) when (Unavailable(e,ct))
        { return StatusCode(503,new {error="Не удалось проверить GitHub или сохранить настройки."}); }
    }

    [HttpDelete("api/v1/managed/project-github"), Authorize(Policy="operator")]
    public async Task<IActionResult> Disconnect(CancellationToken ct)
    {
        if (!team.IsOwner(User)) return Forbid();
        try { await github.Disconnect(ct); return Ok(github.Connection()); }
        catch (Exception e) when (Unavailable(e,ct))
        { return StatusCode(503,new {error="Не удалось удалить настройки GitHub."}); }
    }

    private static bool Unavailable(Exception e, CancellationToken ct) =>
        e is HttpRequestException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException ||
        e is OperationCanceledException && !ct.IsCancellationRequested;

    public sealed record ConnectInput(string Token);

    [AllowAnonymous,HttpGet("api/v1/public/project-release")]
    public async Task<IActionResult> Latest(CancellationToken ct)
    {
        Response.Headers.AccessControlAllowOrigin="*";
        try{return Ok(new {latest=await github.Latest(ct)});}
        catch(Exception e) when(Unavailable(e,ct))
        {return StatusCode(503,new {error="GitHub недоступен."});}
    }

    [HttpGet("api/v1/managed/project-branches"),Authorize(Policy="operator")]
    public async Task<IActionResult> Branches(CancellationToken ct)
    {
        if(!team.IsOwner(User)&&!await team.IsMember(User,ct))return Forbid();
        try{return Ok(await github.Branches(ct));}
        catch(Exception e) when(Unavailable(e,ct))
        {return StatusCode(503,new {error="Не удалось получить ветки GitHub."});}
    }
}
