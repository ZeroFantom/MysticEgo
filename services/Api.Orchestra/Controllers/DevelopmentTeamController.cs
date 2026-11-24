using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/managed/development-team")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class DevelopmentTeamController(DevelopmentTeam team) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Login => User.Identity?.Name ?? "operator";

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var owner = team.IsOwner(User);
        return Ok(new { owner, membership = await team.Find(Actor, ct),
            requests = owner ? await team.All(ct) : [] });
    }

    [HttpPost("apply")]
    public async Task<IActionResult> Apply(CancellationToken ct)
    {
        if (team.IsOwner(User)) return Conflict(new { error = "Вы уже главный разработчик." });
        return await team.Change(Actor, Login, "pending", ct) is { } item
            ? Ok(item) : Conflict(new { error = "Заявка уже отправлена или доступ уже выдан." });
    }

    [HttpPost("{id:guid}/decision")]
    public async Task<IActionResult> Decide(Guid id, Decision input, CancellationToken ct)
    {
        if (!team.IsOwner(User)) return Forbid();
        if (id == Actor || input.Status is not ("approved" or "rejected" or "revoked"))
            return BadRequest(new { error = "Недопустимое решение." });
        return await team.Change(id, Login, input.Status, ct) is { } item
            ? Ok(item) : Conflict(new { error = "Статус заявки изменился. Обновите страницу." });
    }

    public sealed record Decision(string Status);
}
