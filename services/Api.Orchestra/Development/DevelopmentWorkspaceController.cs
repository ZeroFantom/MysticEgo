using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra.Development;

/// <summary>Team-only workspace API. The actor is derived exclusively from the authenticated account.</summary>
[ApiController,Authorize(Policy="operator"),Route("api/v1/managed/development-workspace")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class DevelopmentWorkspaceController(DevelopmentWorkspace workspace,WorkspaceThreads threads,DevelopmentTeam team):ControllerBase
{
    private async Task<Guid?> Actor(CancellationToken ct)=>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)&&
        (team.IsOwner(User)||await team.IsMember(User,ct))?id:null;
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)=>await Actor(ct) is null?Forbid():Ok(await workspace.List(ct));
    [HttpPost,RequestSizeLimit(65536)]
    public Task<IActionResult> Create(DevelopmentWorkspace.CreateInput input,CancellationToken ct)=>Run((actor)=>workspace.Create(actor,input,ct),ct);
    [HttpPost("{id:guid}/{action:regex(^(take|release|complete)$)}"),RequestSizeLimit(1024)]
    public Task<IActionResult> Change(Guid id,string action,CancellationToken ct)=>Run(actor=>workspace.Change(id,actor,team.IsOwner(User),action,ct),ct);
    [HttpPost("{id:guid}/publish"),RequestSizeLimit(1024)]
    public Task<IActionResult> Publish(Guid id,CancellationToken ct)=>Run(actor=>workspace.Publish(id,actor,team.IsOwner(User),ct),ct);
    [HttpGet("{id:guid}/comments")]
    public Task<IActionResult> Comments(Guid id,CancellationToken ct)=>Run(actor=>threads.Read(id,ct),ct);
    [HttpPost("{id:guid}/comments"),RequestSizeLimit(32768)]
    public Task<IActionResult> Comment(Guid id,WorkspaceThreads.CommentInput input,CancellationToken ct)=>Run(actor=>threads.Add(id,actor,User.Identity?.Name??"developer",input,ct),ct);
    [HttpPost("{id:guid}/comments/{commentId:guid}/publish"),RequestSizeLimit(1024)]
    public Task<IActionResult> PublishComment(Guid id,Guid commentId,CancellationToken ct)=>Run(actor=>threads.Publish(id,commentId,actor,team.IsOwner(User),ct),ct);
    private async Task<IActionResult> Run<T>(Func<Guid,Task<T>> action,CancellationToken ct)
    {
        if(await Actor(ct) is not Guid actor)return Forbid();
        try{return Ok(await action(actor));}
        catch(UnauthorizedAccessException){return Forbid();}
        catch(KeyNotFoundException){return NotFound();}
        catch(ArgumentException){return BadRequest(new{code="invalid_workspace_input"});}
        catch(InvalidOperationException){return Conflict(new{code="workspace_action_unavailable"});}
        catch(Exception e) when(e is HttpRequestException or System.Text.Json.JsonException||e is OperationCanceledException&&!ct.IsCancellationRequested)
        {return StatusCode(502,new{code="github_result_uncertain",message="Check synchronization before retrying."});}
    }
}
