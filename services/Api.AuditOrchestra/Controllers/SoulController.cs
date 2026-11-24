using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Soul;

namespace MysticEgo.Api.AuditOrchestra.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/soul")]
public sealed class SoulController(ISoulService souls) : ControllerBase
{
    private string? UserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
    private string? HumanId() => User.Identity?.IsAuthenticated == true && User.FindFirst("token_use")?.Value != "device" ? UserId() : null;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        if (HumanId() is not { } uid) return Forbid();
        return Ok(await souls.Register(uid, ct));
    }

    [HttpPost("activate")]
    public async Task<IActionResult> Activate(CancellationToken ct)
    {
        if (HumanId() is not { } uid) return Forbid();
        return Ok(await souls.Register(uid, ct));
    }

    [HttpGet("questions")]
    public IActionResult Questions() => HumanId() is null ? Forbid() : Ok(new { version = SoulEngine.QuestionnaireVersion, questions = PersonalitySurvey.Items });

    [HttpPost("questions")]
    public Task<IActionResult> Answers([FromBody] Dictionary<string, int> request, CancellationToken ct) =>
        Invoke(uid => souls.SurveyAnswers(uid, request, ct));

    [HttpPost("assistant-card")]
    public Task<IActionResult> Card([FromBody] CardAnswers request, CancellationToken ct) =>
        Invoke(uid => souls.ApplyCard(uid, request, ct));

    [HttpGet("quests")]
    public IActionResult Quests() => HumanId() is null ? Forbid() : Ok(SoulEngine.Quests.Select(x => new { id = x.Key, name = x.Value.Name, npc = x.Value.Npc, prompt = x.Value.Prompt }).ToArray());

    [HttpPost("quests")]
    public Task<IActionResult> Quest([FromBody] QuestAnswers request, CancellationToken ct) =>
        Invoke(uid => souls.Quest(uid, request, ct));

    [HttpPost("memory")]
    public Task<IActionResult> Memory([FromBody] MemoryInput request, CancellationToken ct) =>
        Invoke(uid => souls.Remember(uid, request, ct));

    [HttpGet("context/{mode}")]
    public async Task<IActionResult> Context(string mode, CancellationToken ct)
    {
        if (HumanId() is not { } uid) return Forbid();
        if (mode is not ("game" or "personal")) return BadRequest(new { error = "Invalid assistant context" });
        var result = await souls.Context(uid, mode, ct);
        return result is null ? NotFound() : Ok(result);
    }

    private async Task<IActionResult> Invoke<T>(Func<string, Task<T>> action)
    {
        if (HumanId() is not { } uid) return Forbid();
        try { return Ok(await action(uid)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }
}
