using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Models.Commands;

namespace MysticEgo.Distributed;

internal static class MobileLaunchLocal
{
    internal static readonly MobileLaunchQueue Queue=new();
    internal static string? Owner=>Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");
    internal static bool Enabled=>Environment.GetEnvironmentVariable("MYSTICEGO_MOBILE_LAUNCH_ENABLED")=="true";
    internal static bool Allowed(string? owner)=>Enabled&&!string.IsNullOrWhiteSpace(Owner)&&string.Equals(owner,Owner,StringComparison.Ordinal);
    internal static string? Resolve(string alias)
    {
        if(!MobileLaunchQueue.ValidAlias(alias))return null;
        try{
            var map=JsonSerializer.Deserialize<Dictionary<string,string>>(Environment.GetEnvironmentVariable("MYSTICEGO_LAUNCH_ALLOWLIST_JSON")??"{}");
            if(map is null||!map.TryGetValue(alias,out var path)||string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path)||!File.Exists(path))return null;
            return Path.GetFullPath(path);
        }catch(Exception error) when(error is JsonException or ArgumentException or IOException or NotSupportedException){return null;}
    }
    internal static object View(MobileLaunchQueue.Item item)=>new{item.Id,item.Alias,item.State,item.Expires};
}

[CommandContract("mobile.launch.propose",1)]
[CommandDescription("Create a short-lived launch proposal for local PC owner approval; never launches an app")]
public sealed class MobileLaunchProposeCommand:ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var owner=ctx.CallerLogin;var alias=ctx.RawArgs??"";
        if(!MobileLaunchLocal.Allowed(owner)||!Environment.UserInteractive||MobileLaunchLocal.Resolve(alias) is not {} path)
            return Task.FromResult(CommandResult.Fail("unavailable","Local mobile launch unavailable."));
        ct.ThrowIfCancellationRequested();
        var item=MobileLaunchLocal.Queue.Propose(owner!,alias,path,DateTimeOffset.UtcNow);
        return Task.FromResult(item is null?CommandResult.Fail("busy","Proposal unavailable."):CommandResult.Ok(structured:MobileLaunchLocal.View(item)));
    }
}

[CommandContract("mobile.launch.status",1)]
[CommandDescription("Read status of a mobile launch proposal; no execution")]
public sealed class MobileLaunchStatusCommand:ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        if(!MobileLaunchLocal.Allowed(ctx.CallerLogin)||!Guid.TryParse(ctx.RawArgs,out var id))
            return Task.FromResult(CommandResult.Fail("unavailable","Proposal unavailable."));
        var item=MobileLaunchLocal.Queue.Read(ctx.CallerLogin!,id,DateTimeOffset.UtcNow);
        return Task.FromResult(item is null?CommandResult.Fail("unavailable","Proposal unavailable."):CommandResult.Ok(structured:MobileLaunchLocal.View(item)));
    }
}

[ApiController,Authorize,Route("api/v1/managed/mobile-launch")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class MobileLaunchLocalController(NodeOptions node,IAssistantAccessPolicy assistant,NodeState state):ControllerBase
{
    private string? Principal()
    {
        if(node.Kind is not (NodeKind.Agent or NodeKind.AuditOrchestra)||!Environment.UserInteractive||
            !Guid.TryParse(User.FindFirst("mystic_session")?.Value,out var sid)||sid==Guid.Empty||
            HttpContext.Connection.RemoteIpAddress is not {} address||!IPAddress.IsLoopback(address)||
            Request.Headers.ContainsKey("Forwarded")||Request.Headers.ContainsKey("X-Forwarded-For")||
            !Uri.TryCreate(Request.Scheme+"://"+Request.Host.Value,UriKind.Absolute,out var host)||!host.IsLoopback)return null;
        if(Request.Headers.TryGetValue("Origin",out var origin)&&
            (!Uri.TryCreate(origin.ToString(),UriKind.Absolute,out var url)||!url.IsLoopback||url.Scheme!=Request.Scheme||
                !string.Equals(url.Authority,Request.Host.Value,StringComparison.OrdinalIgnoreCase)))return null;
        var owner=assistant.ResolveOwner(User);
        return MobileLaunchLocal.Allowed(owner)?owner:null;
    }
    [HttpGet]
    public IActionResult List()
    {
        var owner=Principal();return owner is null?Forbid():Ok(MobileLaunchLocal.Queue.List(owner,DateTimeOffset.UtcNow));
    }
    public sealed record Decision(bool Approve,string Alias);
    [HttpPost("{id:guid}/decide"),RequestSizeLimit(1024)]
    public IActionResult Decide(Guid id,Decision input,CancellationToken ct)
    {
        var owner=Principal();if(owner is null)return Forbid();
        var now=DateTimeOffset.UtcNow;
        var current=MobileLaunchLocal.Queue.Read(owner,id,now);
        if(current is null||current.Alias!=input.Alias)return Conflict();
        var item=MobileLaunchLocal.Queue.Decide(owner,id,input.Approve,now);
        if(item is null)return Conflict();
        if(!input.Approve){state.Log(owner,"mobile-launch",id.ToString(),false,"declined");return Ok(MobileLaunchLocal.View(item));}
        bool started=false;
        try{
            if(!ct.IsCancellationRequested&&MobileLaunchLocal.Allowed(owner)&&Environment.UserInteractive&&
                string.Equals(MobileLaunchLocal.Resolve(item.Alias),item.Path,StringComparison.Ordinal))
            {
                using var process=Process.Start(new ProcessStartInfo(item.Path){UseShellExecute=true});
                started=process is not null;
            }
        }catch(Exception e) when(e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException){}
        var result=MobileLaunchLocal.Queue.Finish(owner,id,started,DateTimeOffset.UtcNow);
        state.Log(owner,"mobile-launch",id.ToString(),started,item.Alias);
        return result is null?Conflict():Ok(MobileLaunchLocal.View(result));
    }
}
