using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Api.Orchestra.Development;

namespace MysticEgo.Api.Orchestra.Controllers;

/// <summary>Authenticated team workspace. Provider credentials stay on the server.
/// Each request rechecks membership, including reads and internal operations.</summary>
[ApiController,Authorize(Policy="operator"),Route("api/v1/managed/developer-workspace")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DeveloperWorkspaceController(DevelopmentTeam team,GitHubWorkspace github,InternalWorkspace local):ControllerBase
{
    private Guid Actor=>Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Login=>User.Identity?.Name??"developer";
    private bool Owner=>team.IsOwner(User);
    private async Task<IActionResult> Run(Func<Task<object?>> action,CancellationToken ct)
    {
        if(User.FindFirstValue("token_use")=="device"||!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out _)||
            (!Owner&&!await team.IsMember(User,ct)))return Forbid();
        try{return Ok(await action());}
        catch(ArgumentException){return BadRequest(new{code="invalid_workspace_input"});}
        catch(WorkspaceProviderException e){return StatusCode(e.Status,new{code=e.Code});}
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {return StatusCode(503,new{code="workspace_unavailable"});}
    }

    [HttpGet("connection")]
    public Task<IActionResult> Connection(CancellationToken ct)=>Run(()=>Task.FromResult<object?>(new{github=github.Connection(Actor),owner=Owner,actorId=Actor}),ct);
    [HttpGet("github/{kind}")]
    public Task<IActionResult> List(string kind,CancellationToken ct,[FromQuery]int page=1,[FromQuery]string? cursor=null)=>
        Run(async()=>await github.List(kind,page,cursor,ct),ct);
    [HttpGet("github/categories")]
    public Task<IActionResult> Categories(CancellationToken ct)=>Run(async()=>await github.Categories(ct),ct);
    [HttpGet("github/{kind}/{number:int}")]
    public Task<IActionResult> Detail(string kind,int number,CancellationToken ct,[FromQuery]int page=1)=>
        Run(async()=>await github.Detail(kind,number,page,ct),ct);
    [HttpPost("github/{kind}/{number:int}"),RequestSizeLimit(65536)]
    public Task<IActionResult> Change(string kind,int number,WorkspaceChange input,CancellationToken ct)=>
        Run(async()=>{await github.Change(kind,number,input,Actor,Login,Owner,ct);return new{success=true};},ct);
    [HttpGet("internal")]
    public Task<IActionResult> InternalList(CancellationToken ct)=>Run(async()=>await local.List(ct),ct);
    [HttpPost("internal"),RequestSizeLimit(131072)]
    public Task<IActionResult> Create(WorkspaceCreate input,CancellationToken ct)=>Run(async()=>await local.Create(input,Actor,Login,ct),ct);
    [HttpPost("internal/{id:guid}"),RequestSizeLimit(16384)]
    public Task<IActionResult> InternalChange(Guid id,WorkspaceChange input,CancellationToken ct)=>
        Run(async()=>await local.Change(id,input,Actor,Login,Owner,ct),ct);
    public sealed record ReconcileRequest(int Number);
    [HttpPost("internal/{id:guid}/reconcile"),RequestSizeLimit(1024)]
    public Task<IActionResult> Reconcile(Guid id,ReconcileRequest input,CancellationToken ct)=>
        Run(async()=>Owner?await local.Reconcile(id,input.Number,Login,ct):throw new WorkspaceProviderException(403,"owner_required"),ct);
    public sealed record PublishRequest(bool Confirmed,string? Category);
    [HttpPost("internal/{id:guid}/publish"),RequestSizeLimit(4096)]
    public Task<IActionResult> Publish(Guid id,PublishRequest input,CancellationToken ct)=>
        Run(async()=>input.Confirmed?await local.Publish(id,input.Category,Actor,Login,Owner,ct):
            throw new ArgumentException("publication_confirmation_required"),ct);
}
