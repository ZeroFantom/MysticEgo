using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Global.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/vpn")]
public sealed class VpnController(IVpnService vpn,IVpnAccessPolicy access) : ControllerBase
{
    private string? Actor() => access.Actor(User);
    private bool Admin() => access.CanManage(User,false);
    private async Task<IActionResult> Invoke<T>(Func<Task<T>> task)
    {
        try { return Ok(await task()); }
        catch(NodeApiException ex) { return StatusCode(ex.StatusCode,new{error=ex.Message}); }
    }
    private IActionResult Invoke(Func<object> task)
    {
        try { return Ok(task()); }
        catch(NodeApiException ex) { return StatusCode(ex.StatusCode,new{error=ex.Message}); }
    }

    [HttpGet("me")]
    public Task<IActionResult> Me(CancellationToken ct) => Actor() is { } user ? Invoke(()=>vpn.MyRequests(user,ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpPost("requests")]
    public Task<IActionResult> Apply([FromBody] VpnApply request,CancellationToken ct) => Actor() is { } user
        ? Invoke(()=>vpn.Apply(user,request,ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpGet("requests/{id:guid}/subscription")]
    public async Task<IActionResult> Subscription(Guid id,CancellationToken ct)
    {
        if(Actor() is not { } user)return Forbid();
        Response.Headers.CacheControl="no-store";
        return await Invoke(()=>vpn.Subscription(user,id,ct));
    }

    [HttpGet("admin/requests")]
    public Task<IActionResult> Requests(CancellationToken ct) => Admin() ? Invoke(()=>vpn.Requests(ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpPut("admin/requests/{id:guid}")]
    public Task<IActionResult> Decision(Guid id,[FromBody] VpnDecision decision,CancellationToken ct) => Admin()
        ? Invoke(()=>vpn.Decide(id,decision,Actor()!,ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpPost("admin/deployments/plan")]
    public IActionResult Plan([FromBody] VpnDeployDraft draft) => Admin() ? Invoke(()=>vpn.DeploymentPlan(draft)) : Forbid();

    [HttpGet("admin/panels")]
    public Task<IActionResult> Panels(CancellationToken ct) => Admin() ? Invoke(()=>vpn.Panels(ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpPost("admin/panels")]
    public async Task<IActionResult> AddPanel([FromBody] VpnPanelCreate request,CancellationToken ct)
    {
        if(!Admin())return Forbid();
        try { return Created("/api/v1/vpn/admin/panels",await vpn.AddPanel(request,Actor()!,ct)); }
        catch(NodeApiException ex) { return StatusCode(ex.StatusCode,new{error=ex.Message}); }
    }
}
