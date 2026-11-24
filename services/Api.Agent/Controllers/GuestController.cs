using System.Net;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Agent.Controllers;

[ApiController]
[Route("api/v1/guest")]
public sealed class GuestController(GuestSessions guests, NodeOptions node, IHttpClientFactory factory, ILmStudioClient model) : ControllerBase
{
    [HttpGet("availability")]
    public async Task<IActionResult> Availability(CancellationToken ct) =>
        Ok(new { available = ClubLan.PrivateAddress(HttpContext.Connection.RemoteIpAddress) && await ClubLan.ClubPresent(node, factory, ct),
            club = node.ClubDescription, localId = node.LocalId, globalId = node.GlobalId });

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] CreateGuest request, CancellationToken ct)
    {
        if (!ClubLan.PrivateAddress(HttpContext.Connection.RemoteIpAddress) || !await ClubLan.ClubPresent(node, factory, ct))
            return StatusCode(403, new { error = "Временная сессия доступна на подключённом ПК клуба." });
        var nickname=(request.Nickname??"").Trim();
        if(nickname.Length is <2 or >30||nickname.Any(char.IsControl))return BadRequest(new{error="Никнейм 2–30 символов."});
        try
        {
            var (token,guest)=guests.Create(nickname);
            Response.Headers["Cache-Control"]="no-store";
            return Ok(new{token,guest,scope="local-guest",persistent=false});
        }
        catch(InvalidOperationException ex){return StatusCode(429,new{error=ex.Message});}
    }
    [HttpPost("assistant")]
    public async Task<IActionResult> Assistant(AssistantWorkbenchController.TextInput input, CancellationToken ct)
    {
        if (guests.Find(Request.Headers["X-Guest-Session"].ToString()) is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(input.Text) || input.Text.Length > 1200) return BadRequest();
        var context = "Ты помощник гостевого ПК. Отвечай только текстом. Вопросы о клубе: " +
            (node.ClubDescription ?? "Сведений нет.") + " Адрес клуба: " + node.ClubAddress + ". /no_think";
        try { return Ok(new { text = await model.TextAsync(input.Text, context, ct) }); }
        catch (AssistantApiException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
    }

    [HttpGet("me")]
    public IActionResult Me()
    {
        var guest=guests.Find(Request.Headers["X-Guest-Session"].ToString());
        return guest is null?Unauthorized():Ok(guest);
    }
    [HttpPost("logout")]
    public IActionResult Logout()
    {
        guests.Revoke(Request.Headers["X-Guest-Session"].ToString());
        return NoContent();
    }
}
