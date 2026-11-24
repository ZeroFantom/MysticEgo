using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.WorldAI;

[ApiController]
[Authorize]
[Route("api/v1/world-ai")]
public sealed class WorldAiController(WorldAiCluster cluster) : ControllerBase
{
    [HttpGet("workers")]
    public IActionResult Workers() => Ok(cluster.Workers());

    [HttpGet("workers/status")]
    public async Task<IActionResult> WorkerStatus(CancellationToken ct) => Ok(await cluster.WorkerStatus(ct));

    [HttpPost("workers")]
    public async Task<IActionResult> Register([FromBody] RegisterWorker request, CancellationToken ct)
    {
        try { return Ok(await cluster.Register(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("memory")]
    public IActionResult Memory() => Ok(cluster.Memory());

    [HttpPost("memory")]
    public async Task<IActionResult> AddFact([FromBody] RecordFact request, CancellationToken ct)
    {
        try { return Ok(await cluster.AddFact(request.Kind, request.Content, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("chat")]
    public async Task<IActionResult> Chat([FromBody] ChatRequest request, CancellationToken ct)
    {
        try { return Ok(await cluster.Chat(request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Problem(ex.Message, statusCode: 503); }
    }
}

[ApiController]
[Authorize]
[Route("api/v1/world-ai/jobs")]
public sealed class WorldAiJobsController(WorldAiJobQueue queue) : ControllerBase
{
    [HttpPost("")]
    public async Task<IActionResult> Enqueue([FromBody] EnqueueWorldAiRequest request, CancellationToken ct)
    {
        try
        {
            var job = await queue.Enqueue(request, ct);
            return Accepted($"/api/v1/world-ai/jobs/{job.Id}", new { job.Id, job.State });
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (WorldAiStorageUnavailableException) { return StatusCode(503); }
        catch (InvalidOperationException) { return StatusCode(429); }
    }

    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(await queue.Status(ct));

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(string id, CancellationToken ct)
    {
        var job = await queue.Find(id, ct);
        return job is null ? NotFound() : Ok(job);
    }
}
