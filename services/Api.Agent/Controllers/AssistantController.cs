using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Agent.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/assistant")]
public sealed class AssistantController(ILocalAssistantService assistant, IAssistantAccessPolicy access) : ControllerBase
{
    private string? Actor() => access.ResolveOwner(User);

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        if (Actor() is null) return Forbid();
        return Ok(await assistant.StatusAsync(ct));
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] LmChatRequest request, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Forbid();
        try { return Ok(await assistant.ChatAsync(actor, request, ct)); }
        catch (AssistantApiException ex) { return Problem(ex.Message, statusCode: ex.StatusCode); }
    }

    [HttpPost("proposals/{id:guid}/confirm")]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var actor = Actor();
        if (actor is null) return Forbid();
        try { return Ok(await assistant.ConfirmAsync(actor, id, ct)); }
        catch (AssistantApiException ex) { return Problem(ex.Message, statusCode: ex.StatusCode); }
    }
}
