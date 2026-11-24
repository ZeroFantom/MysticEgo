using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/orchestra/update")]
public sealed class OrchestraUpdateController(NodeState state, DevelopmentTeam team) : ControllerBase
{
    private const string StateDirectory = "/var/lib/mysticego-orchestra-updater";
    private const string Inbox = "/var/lib/mysticego/update-inbox";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static UpdateStatus? ReadStatus() =>
        System.IO.File.Exists(StateDirectory + "/status.json")
            ? JsonSerializer.Deserialize<UpdateStatus>(System.IO.File.ReadAllText(StateDirectory + "/status.json"), Json)
            : null;

    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Status()
    {
        if (!team.IsOwner(User)) return Forbid();
        var status = ReadStatus();
        return status is null ? StatusCode(503, new { error = "Служба обновления Orchestra ещё не настроена." }) : Ok(status);
    }

    [HttpPost, RequestSizeLimit(4096)]
    public IActionResult Approve(Approval request)
    {
        if (!team.IsOwner(User)) return Forbid();
        var status = ReadStatus();
        var target = request.Target ?? "orchestra";
        if (target is not ("orchestra" or "audit" or "both"))
            return BadRequest(new { error = "Выберите Orchestra, Audit или оба компонента." });
        var allowed = status?.Protocol == 2
            ? status.Targets?.Contains(target, StringComparer.Ordinal) == true
            : target == "orchestra";
        if (!request.Confirmed || !allowed || status?.Candidate is not { } candidate ||
            status.Phase is not ("ready" or "failed") || candidate.Tag != request.Tag || candidate.Digest != request.Digest)
            return Conflict(new { error = "Заново проверьте релиз и подтвердите его версию." });

        var path = Path.Combine(Inbox, "pending.json");
        var temporary = Path.Combine(Inbox, Guid.NewGuid().ToString("N") + ".tmp");
        if (!Directory.Exists(Inbox)) return StatusCode(503, new { error = "Служба обновления не настроена." });
        try
        {
            System.IO.File.WriteAllText(temporary, status.Protocol == 2
                ? JsonSerializer.Serialize(new UpdateRequest(target, candidate), Json)
                : JsonSerializer.Serialize(candidate, Json));
            try { System.IO.File.Move(temporary, path); }
            catch (IOException) when (System.IO.File.Exists(path))
            { return Conflict(new { error = "Обновление уже ожидает запуска." }); }
            state.Log(User.Identity?.Name ?? "developer", "orchestra.update.approved", candidate.Tag + " " + target, true);
            return Accepted();
        }
        finally { System.IO.File.Delete(temporary); }
    }

    public sealed record Approval(string Tag, string Digest, bool Confirmed, string? Target = null);
    private sealed record UpdateRequest(string Target, Candidate Candidate);
    public sealed record Candidate(string Tag, string Digest, string Asset, long Id, long Size);
    public sealed record UpdateStatus(string Phase, string? Current, Candidate? Candidate, string? Message,
        int Protocol = 0, string? CurrentAudit = null, string[]? Targets = null, string? LastTarget = null);
}
