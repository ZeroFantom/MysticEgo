using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Soul;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController,AllowAnonymous,Route("api/mini/soul")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MiniSoulController(IConfiguration configuration,MiniSoulIdentity identity,SoulMirrorReader reader,WorldPresenceFeedReader presence):ControllerBase
{
    [HttpPost,Models.Commands.SkipTool,RequestSizeLimit(1024)]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
        if(!configuration.GetValue<bool>("MiniApp:SoulReadEnabled"))return NotFound();
        if(!Uri.TryCreate(configuration["MiniApp:Origin"],UriKind.Absolute,out var origin)||
            origin.Scheme!="https"||origin.UserInfo.Length!=0||origin.AbsolutePath!="/"||
            origin.Query.Length!=0||origin.Fragment.Length!=0||Request.Headers.Origin.Count!=1||
            Request.Headers.Origin.ToString()!=origin.GetLeftPart(UriPartial.Authority))return StatusCode(403);
        var header=Request.Headers["X-Mystic-Mini-Session"];
        if(header.Count!=1||!MiniSoulIdentity.ValidCapability(header.ToString()))return Unauthorized();
        var token=header.ToString();
        var principal=await identity.Resolve(token,ct);
        if(principal.UserId is not Guid account)return principal.Denied?Unauthorized():StatusCode(503);
        SoulMirrorView? view;
        try{view=await reader.Read(account,ct);}
        catch(Exception ex) when(ex is IOException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException)
        {return StatusCode(503);}
        // Re-check linkage after reading, before disclosing any personal projection.
        var live=await identity.Resolve(token,ct);
        if(live.UserId!=account)return live.Denied?Unauthorized():StatusCode(503);
        return Ok(new{exists=view is not null,soul=view,placementAvailable=false});
    }

    [HttpPost("/api/mini/presence"),Models.Commands.SkipTool,RequestSizeLimit(1024)]
    public async Task<IActionResult> Presence(CancellationToken ct)
    {
        Response.Headers.CacheControl="no-store";
        Response.Headers["Referrer-Policy"]="no-referrer";
        if(!configuration.GetValue<bool>("MiniApp:SoulReadEnabled")||
            !configuration.GetValue<bool>("MiniApp:PresenceReadEnabled"))return NotFound();
        if(!Uri.TryCreate(configuration["MiniApp:Origin"],UriKind.Absolute,out var origin)||
            origin.Scheme!="https"||origin.UserInfo.Length!=0||origin.AbsolutePath!="/"||
            origin.Query.Length!=0||origin.Fragment.Length!=0||Request.Headers.Origin.Count!=1||
            Request.Headers.Origin.ToString()!=origin.GetLeftPart(UriPartial.Authority))return StatusCode(403);
        var header=Request.Headers["X-Mystic-Mini-Session"];
        if(header.Count!=1||!MiniSoulIdentity.ValidCapability(header.ToString()))return Unauthorized();
        var token=header.ToString();
        var principal=await identity.Resolve(token,ct);
        if(principal.UserId is not Guid account)return principal.Denied?Unauthorized():StatusCode(503);
        var started=System.Diagnostics.Stopwatch.StartNew();
        var view=await presence.ReadConfigured(configuration["WorldAuthority:DatabasePath"],configuration["MiniApp:PresenceFeedPath"],account,ct);
        var live=await identity.Resolve(token,ct);
        if(live.UserId!=account)return live.Denied?Unauthorized():StatusCode(503);
        var remaining=view.FreshForSeconds-(int)Math.Ceiling(started.Elapsed.TotalSeconds);
        return Ok(remaining>0?view with {FreshForSeconds=remaining}:new WorldPresenceRead(false,null,0));
    }
}
