using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Distributed;

[ApiController]
[Route("health")]
public sealed class NodeHealthController(NodeOptions node) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "alive", node = node.NodeId, kind = node.Kind.ToString() });

    [AllowAnonymous]
    [HttpGet("link")]
    public IActionResult Link() => File(LinkSample, "application/octet-stream");
    private static readonly byte[] LinkSample = System.Security.Cryptography.RandomNumberGenerator.GetBytes(65_536);

    [AllowAnonymous]
    [HttpGet("ready")]
    public IActionResult Ready() => Ok(new { status = "ready", node = node.NodeId,
        note = "Optional peer failures do not affect local readiness" });
}

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class NodeController(INodeAccessPolicy access, INodeQueryService queries,
    OptionalDependencyState dependencies, NodeOptions options, NodeState state) : ControllerBase
{
    [HttpGet("diagnostics/capabilities")]
    public IActionResult CapabilitiesHealth() => access.Operator(User)
        ? Ok(new { node=options.NodeId, components=dependencies.Snapshot() }) : Forbid();

    [HttpGet("deployment")]
    public IActionResult Deployment() => access.Operator(User) ? Ok(queries.Deployment()) : Forbid();

    [HttpGet("diagnostics/ping")]
    public IActionResult Ping() => access.Operator(User) ? Ok(queries.Ping()) : Forbid();

    [HttpGet("diagnostics/links")]
    public async Task<IActionResult> Links(CancellationToken ct) => access.Admin(User)
        ? Ok(await queries.Links(ct)) : Forbid();

    [HttpGet("diagnostics/payload")]
    public IActionResult Payload([FromQuery] int bytes = 65536)
    {
        if (!access.Admin(User)) return Forbid();
        if (bytes is < 1 or > 1_048_576) return BadRequest(new { error="payload_out_of_range" });
        return File(new byte[bytes], "application/octet-stream", enableRangeProcessing:false);
    }

    [HttpGet("meta")]
    public IActionResult Meta() => access.Operator(User) ? Ok(queries.Meta()) : Forbid();

    [HttpGet("overview")]
    public async Task<IActionResult> Overview(CancellationToken ct) => access.Operator(User)
        ? Ok(await queries.Overview(ct)) : Forbid();

    [HttpGet("nodes")]
    public IActionResult Nodes() => access.Operator(User) ? Ok(queries.Nodes()) : Forbid();

    [HttpPost("nodes/announce")]
    public IActionResult Announce([FromBody] NodeAnnounce announcement)
    {
        if (!access.Peer(User) || User.FindFirst("identity_id")?.Value != announcement.Id) return Forbid();
        var kind = User.FindFirst("identity_kind")?.Value;
        if (kind == "audit" ? announcement.Kind != nameof(NodeKind.AuditOrchestra)
            : !string.Equals(kind, announcement.Kind, StringComparison.OrdinalIgnoreCase)) return Forbid();
        try { return Ok(queries.Announce(announcement)); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }

    [HttpGet("agents")]
    public async Task<IActionResult> Agents() => access.Operator(User) ? Ok(await queries.Agents()) : Forbid();

    [HttpGet("agents/{id:guid}/capabilities")]
    public async Task<IActionResult> AgentCapabilities(Guid id,CancellationToken ct) => access.Operator(User)
        ? Ok(await queries.Capabilities(id,ct)) : Forbid();

    [HttpGet("audit/page")]
    public async Task<IActionResult> AuditPage([FromQuery] int page = 1, [FromQuery] int size = 25,
        [FromQuery] string? q = null, [FromQuery] bool? success = null, [FromQuery] string? target = null, CancellationToken ct = default) =>
        access.Admin(User) ? Ok(await state.Journal.Read(page, size, q, success, target, ct)) : Forbid();

    [HttpGet("audit")]
    public IActionResult Audit() => access.Admin(User) ? Ok(queries.Audit()) : Forbid();
}

[ApiController]
[Authorize]
[Route("api/v1/commands")]
public sealed class NodeCommandsController(INodeAccessPolicy access, INodeCommandService commands) : ControllerBase
{
    [HttpPost("dispatch")]
    public async Task<IActionResult> Dispatch([FromBody] CommandRequest request,CancellationToken ct)
    {
        if (!access.CanCommand(User) || !access.SensitiveAllowed(User,request.CommandId,request.ConfirmedByHuman)) return Forbid();
        try { return Ok(await commands.Dispatch(request,User.Identity?.Name,ct)); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }

    [HttpPost("bulk")]
    public async Task<IActionResult> Bulk([FromBody] BulkCommandRequest request,CancellationToken ct)
    {
        // BulkCommandRequest has no ConfirmedByHuman flag: sensitive commands are never silently confirmed.
        if (!access.CanCommand(User) || !access.SensitiveAllowed(User,request.CommandId,false)) return Forbid();
        try { return Ok(await commands.Bulk(request,User.Identity?.Name,ct)); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }
}

[ApiController]
[Authorize]
[Route("api/v1/records")]
public sealed class NodeRecordsController(INodeAccessPolicy access,INodeRecordService records) : ControllerBase
{
    [HttpGet("{collection}")]
    public async Task<IActionResult> List(string collection,CancellationToken ct)
    {
        if (!access.Operator(User)) return Forbid();
        try { return Ok(await records.List(collection,ct)); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }

    [HttpPost("{collection}")]
    public async Task<IActionResult> Create(string collection,[FromBody] SaveRecord request,CancellationToken ct)
    {
        if (!access.Admin(User)) return Forbid();
        try
        {
            var record=await records.Create(collection,request,User.Identity?.Name??"operator",ct);
            return Created($"/api/v1/records/{collection}/{record.Id}",record);
        }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }

    [HttpPut("{collection}/{id:guid}")]
    public async Task<IActionResult> Update(string collection,Guid id,[FromBody] SaveRecord request,CancellationToken ct)
    {
        if (!access.Admin(User)) return Forbid();
        try { return Ok(await records.Update(collection,id,request,User.Identity?.Name??"operator",ct)); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }

    [HttpDelete("{collection}/{id:guid}")]
    public async Task<IActionResult> Delete(string collection,Guid id,CancellationToken ct)
    {
        if (!access.Admin(User)) return Forbid();
        try { await records.Delete(collection,id,User.Identity?.Name??"operator",ct); return NoContent(); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode,new {error=ex.Message}); }
    }
}
