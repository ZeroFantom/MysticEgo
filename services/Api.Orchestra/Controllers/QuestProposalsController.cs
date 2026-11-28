using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;
using MysticEgo.WorldAI;

namespace MysticEgo.Api.Orchestra.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RequestQuestProposal(Guid RequestId, string FactId);

/// <summary>Owner review of WorldAI narrative drafts for the current Orchestra.</summary>
[ApiController]
[Authorize(Policy = "operator")]
[Route("api/v1/world-ai/quest-proposals")]
public sealed class QuestProposalsController(DevelopmentTeam team, NodeOptions node,
    WorldAiJobQueue queue, WorldAiCluster cluster) : ControllerBase
{
    private bool IsOwner(out Guid account) =>
        Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out account) &&
        account != Guid.Empty && team.IsOwner(User);

    [HttpPost]
    public async Task<IActionResult> Create(RequestQuestProposal input, CancellationToken ct)
    {
        if (!IsOwner(out var account)) return Forbid();
        if (input.RequestId == Guid.Empty || !Guid.TryParseExact(input.FactId, "N", out var factId) || factId == Guid.Empty)
            return BadRequest(new { error = "requestId and factId are required." });

        var scope = new QuestDraftScope(account, node.NodeId, input.RequestId, factId.ToString("N"));
        try
        {
            var job = await queue.EnqueueQuest(scope, cluster, ct);
            return AcceptedAtAction(nameof(Get), new { id = job.Id }, new { job.Id, job.State });
        }
        catch (QuestRequestConflictException) { return Conflict(new { error = "Request already used for another fact." }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (WorldAiStorageUnavailableException) { return StatusCode(503); }
        catch (IOException) { return StatusCode(503); }
        catch (UnauthorizedAccessException) { return StatusCode(503); }
        catch (InvalidOperationException) { return StatusCode(429); }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        if (!IsOwner(out var account)) return Forbid();
        var job = await queue.Find(id, ct);
        if (job?.QuestDraft is not { } scope || scope.Account != account || scope.World != node.NodeId)
            return NotFound();

        Response.Headers.CacheControl = "no-store";
        return Ok(new { job.Id, job.State, job.ErrorCode, proposal = job.Result is { } json
            ? JsonSerializer.Deserialize<JsonElement>(json) : (JsonElement?)null });
    }
}
