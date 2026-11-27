using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra.Development;

/// <summary>Verify the raw GitHub webhook body using a configured secret and a constant-time HMAC comparison.</summary>
public static class WorkspaceWebhookSignature
{
    public static bool Verify(string? secret,string? signature,ReadOnlySpan<byte> body)
    {
        if(string.IsNullOrWhiteSpace(secret)||secret.Length<32||signature is null||signature.Length!=71||!signature.StartsWith("sha256=",StringComparison.Ordinal))return false;
        try
        {
            var actual=Convert.FromHexString(signature[7..]);
            var expected=HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret),body);
            return CryptographicOperations.FixedTimeEquals(actual,expected);
        }
        catch(FormatException){return false;}
    }
}

/// <summary>Coalesce verified event bursts. A webhook only requests a fresh read, never authorizes a write.</summary>
public sealed class WorkspaceSyncSignal
{
    private int pending;
    public void Request()=>Interlocked.Exchange(ref pending,1);
    public bool Consume()=>Interlocked.Exchange(ref pending,0)==1;
}

[ApiController,AllowAnonymous,Route("api/v1/public/development-webhook")]
public sealed class WorkspaceWebhookController(IConfiguration config,WorkspaceSyncSignal signal):ControllerBase
{
    [HttpPost,RequestSizeLimit(1048576)]
    public async Task<IActionResult> Post(CancellationToken ct)
    {
        var secret=config["DevelopmentGitHub:WebhookSecret"];
        if(string.IsNullOrWhiteSpace(secret)||secret.Length<32)return NotFound();
        if(Request.ContentLength>1048576)return StatusCode(413);
        using var bytes=new MemoryStream();
        var buffer=new byte[8192];
        int count;
        while((count=await Request.Body.ReadAsync(buffer,ct))>0)
        {
            if(bytes.Length+count>1048576)return StatusCode(413);
            await bytes.WriteAsync(buffer.AsMemory(0,count),ct);
        }
        var body=bytes.ToArray();
        if(!WorkspaceWebhookSignature.Verify(secret,Request.Headers["X-Hub-Signature-256"].ToString(),body))return Unauthorized();
        try
        {
            using var json=JsonDocument.Parse(body);
            if(!json.RootElement.TryGetProperty("repository",out var repository)||
               !repository.TryGetProperty("full_name",out var name)||name.ValueKind!=JsonValueKind.String||
               !string.Equals(name.GetString(),GitHubPanel.Repository,StringComparison.OrdinalIgnoreCase))return Forbid();
            var kind=Request.Headers["X-GitHub-Event"].ToString();
            if(kind is "issues" or "issue_comment" or "discussion" or "discussion_comment")signal.Request();
            return Accepted();
        }
        catch(JsonException){return BadRequest();}
    }
}
