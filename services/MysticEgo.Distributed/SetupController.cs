using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Commands;

namespace MysticEgo.Distributed;

[ApiController]
[Authorize]
[Route("api/v1/setup")]
public sealed class SetupController(ISetupService service,ISetupAccessPolicy policy) : ControllerBase
{
    private bool Manager() => policy.Manager(User);
    private string? Human() => policy.Human(User);
    private async Task<IActionResult> Invoke<T>(Func<Task<T>> task)
    {
        try{return Ok(await task());}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpGet("state")]
    public Task<IActionResult> State(CancellationToken ct) => Manager()?Invoke(()=>service.State(ct)):Task.FromResult<IActionResult>(Forbid());
    [HttpPost("parent/connect")]
    public Task<IActionResult> ConnectParent([FromBody] SetupJoinParentRequest request,CancellationToken ct) => Manager()
        ? Invoke(()=>service.ConnectParent(request,Human()??"unknown",ct)):Task.FromResult<IActionResult>(Forbid());
    [HttpGet("organization")]
    public Task<IActionResult> Organization(CancellationToken ct) => Manager()
        ? Invoke(()=>service.Organization(ct)):Task.FromResult<IActionResult>(Forbid());
    [HttpPost("organization")]
    [ApiCapability("setup.organization.create","Первоначальная настройка команды/сети","control",RequiresConfirmation=true)]
    public async Task<IActionResult> CreateOrganization([FromBody] SetupOrganizationRequest request,CancellationToken ct)
    {
        if(!Manager())return Forbid();
        try{return Created("/api/v1/setup/organization",await service.CreateOrganization(request,Human()??"unknown",ct));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpPost("team/invitations")]
    public Task<IActionResult> TeamInvitation([FromBody] CreateTeamInviteRequest request,CancellationToken ct) => Manager()
        ? Invoke(()=>service.InviteTeam(request,Human()??"unknown",ct)):Task.FromResult<IActionResult>(Forbid());
    [HttpPost("team/join")]
    public Task<IActionResult> JoinTeam([FromBody] JoinTeamRequest request,CancellationToken ct) => Human() is { } actor
        ? Invoke(()=>service.JoinTeam(request,actor,ct)):Task.FromResult<IActionResult>(Forbid());
    [HttpPost("invitations")]
    public Task<IActionResult> InviteNode([FromBody] SetupInviteRequest request,CancellationToken ct) => Manager()
        ? Invoke(()=>service.InviteNode(request,Human()??"unknown",ct)):Task.FromResult<IActionResult>(Forbid());
    [AllowAnonymous]
    [HttpPost("enroll")]
    public Task<IActionResult> Enroll([FromBody] SetupEnrollRequest request,CancellationToken ct) =>
        Invoke(()=>service.Enroll(request,HttpContext.Connection.RemoteIpAddress,Request.IsHttps,ct));
}
