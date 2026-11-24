using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Global.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/social")]
public sealed class SocialController(ISocialService social,ISocialAccessPolicy access) : ControllerBase
{
    private string? Actor() => access.Actor(User);
    private async Task<IActionResult> Invoke<T>(Func<string,Task<T>> action)
    {
        if(Actor() is not { } actor)return Forbid();
        try{return Ok(await action(actor));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }

    [HttpGet("directory")]
    public Task<IActionResult> Directory() => Invoke(_=>social.Directory(HttpContext.RequestAborted));
    [HttpPut("affiliations")]
    public Task<IActionResult> Affiliation([FromBody] EmployeeAffiliationEdit request)
    {
        if(!access.OrganizationAdmin(User))return Task.FromResult<IActionResult>(Forbid());
        return Invoke(actor=>social.SetAffiliation(request,actor,HttpContext.RequestAborted));
    }
    [HttpPut("role-appearance")]
    public Task<IActionResult> RoleAppearance([FromBody] RoleAppearanceEdit request)
    {
        if(!access.Developer(User))return Task.FromResult<IActionResult>(Forbid());
        return Invoke(_=>social.SetAppearance(request,HttpContext.RequestAborted));
    }
    [HttpGet("profile/me")]
    public Task<IActionResult> Me() => Invoke(actor=>social.Profile(actor,access.Level(User),HttpContext.RequestAborted));
    [HttpPut("profile/me")]
    public Task<IActionResult> UpdateMe([FromBody] ProfileEdit request) => Invoke(actor=>social.UpdateProfile(actor,access.Level(User),request,HttpContext.RequestAborted));
    [HttpGet("notifications")]
    public Task<IActionResult> Notifications() => Invoke(actor=>social.Notifications(actor,HttpContext.RequestAborted));
    [HttpPost("notifications/{id:guid}/read")]
    public Task<IActionResult> Read(Guid id) => Invoke(actor=>social.ReadNotification(actor,id,HttpContext.RequestAborted));
    [HttpGet("friends")]
    public Task<IActionResult> Friends() => Invoke(actor=>social.Friends(actor,HttpContext.RequestAborted));
    [HttpPost("friends/request")]
    public Task<IActionResult> FriendRequest([FromBody] FriendEdit request) => Invoke(actor=>social.RequestFriend(actor,request,HttpContext.RequestAborted));
    [HttpPost("friends/{id:guid}/accept")]
    public Task<IActionResult> Accept(Guid id) => Invoke(actor=>social.AcceptFriend(actor,id,HttpContext.RequestAborted));
    [HttpGet("messages")]
    public Task<IActionResult> Messages() => Invoke(actor=>social.Messages(actor,HttpContext.RequestAborted));
    [HttpPost("messages")]
    public Task<IActionResult> Message([FromBody] MessageEdit request) => Invoke(actor=>social.SendMessage(actor,request,HttpContext.RequestAborted));
}
