using Models.Commands;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/managed")]
public sealed class ManagedNetworkController(ManagedNetwork network, ManagedDeployment deployment, NodeState state, ParentAuthorization peers, DevelopmentTeam team) : ControllerBase
{
    private Guid Actor() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private string Login => User.Identity?.Name ?? "operator";
    private bool Owner => team.IsOwner(User);
    private bool TeamMember => HttpContext.Items.ContainsKey(typeof(DevelopmentTeam));
    private bool Developer => Owner || TeamMember;
    private bool MayManageAsDeveloper(Guid id) => Owner || TeamMember && network.Find(id)?.Kind == "global";

    [HttpGet("context")]
    public IActionResult Context() => Ok(new { rootId = network.RootId, rootName = state.Options.DisplayName,
        orchestraUrl = network.OrchestraUrl, authUrl = network.AuthUrl, developer = Developer, owner = Owner, actor = Actor(),
        lan = state.Options.AllowInsecureTestHttp });

    [HttpGet("topology")]
    [ApiCapability("managed.topology.read", "Загрузить доступные узлы и связи между ними", "all")]
    public IActionResult Topology() => Ok(network.Topology(Actor(), Developer).Where(item => !TeamMember || item.Node.Kind != "audit" || item.Node.OwnerId == Actor()));

    [HttpGet("access/{id:guid}")]
    public IActionResult Access(Guid id)
    {
        var node = network.Find(id);
        return node is not null && node.Enabled && (!TeamMember || node.Kind != "audit" || node.OwnerId == Actor()) && network.CanRead(node, Actor(), Developer)
            ? Ok(new { canRead = true, canManage = network.CanManage(node, Actor(), MayManageAsDeveloper(node.Id)) }) : NotFound();
    }

    [HttpGet("applications")]
    public async Task<IActionResult> Applications(CancellationToken ct) => Ok(await network.Applications(Actor(), Developer, ct));

    [HttpPost("applications")]
    [ApiCapability("managed.application.create", "Подать заявку на Global своей организации", "audit", RequiresConfirmation = true)]
    public Task<IActionResult> Apply(ApplicationInput input, CancellationToken ct) =>
        Run(() => network.Apply(Actor(), Login, input.Name, input.Description, input.Address, input.Contact, input.Website, ct));

    [HttpPost("applications/{id:guid}/decision")]
    [ApiCapability("managed.application.review", "Принять решение по заявке Global", "orchestra", "system.bypass", RequiresConfirmation = true)]
    public Task<IActionResult> Decide(Guid id, DecisionInput input, CancellationToken ct) => Developer
        ? Run(() => network.Decide(id, input.Approved, input.Note, Login, ct))
        : Task.FromResult<IActionResult>(Forbid());

    [HttpPost("nodes")]
    [ApiCapability("managed.deployment.prepare", "Подготовить конфигурацию Global или Local", "management", RequiresConfirmation = true)]
    public Task<IActionResult> Create(NewManagedNode input, CancellationToken ct) =>
        Run(() => network.Create(input, Actor(), Login, Owner, Parent(), Request.Headers.Authorization.ToString(), ct));

    [HttpPost("home/connect"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> Home(HomeInput input, CancellationToken ct) =>
        Run(() => network.AttachHome(Actor(), Login, input.Machine, ct));

    [HttpPost("club/assignment"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public Task<IActionResult> Assignment(CancellationToken ct) =>
        Parent("local") is { } local ? Run(() => network.IssueClub(local, Actor(), Login, Owner, ct)) :
            Task.FromResult<IActionResult>(Forbid());

    [HttpPut("nodes/{id:guid}")]
    public Task<IActionResult> Edit(Guid id, ManagedNodeEdit input, CancellationToken ct) =>
        Run(() => network.Edit(id, input, Actor(), Login, MayManageAsDeveloper(id), ct));

    [HttpPut("nodes/{id:guid}/members/{member:guid}")]
    public Task<IActionResult> Member(Guid id, Guid member, MemberInput input, CancellationToken ct) =>
        Run(() => network.SetMember(id, member, input.Allowed, Actor(), Login, MayManageAsDeveloper(id), ct));

    [HttpGet("nodes/{id:guid}/profile"), ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Profile(Guid id)
    {
        var node = network.Find(id);
        if (node is null || node.Kind is "audit" or "agent" || !network.CanManage(node, Actor(), MayManageAsDeveloper(node.Id)) ||
            node.Kind == "local" && Parent("global") != node.ParentId || !System.IO.File.Exists(network.ProfilePath(id))) return NotFound();
        state.Log(Login, "deployment.profile.download", id.ToString(), true);
        return PhysicalFile(network.ProfilePath(id), "application/json", $"mysticego-{node.Kind}-{id:N}.json");
    }

    [HttpPost("nodes/{id:guid}/preflight")]
    [ApiCapability("managed.deployment.check", "Проверить SSH и Docker на выбранном целевом компьютере", "management")]
    public Task<IActionResult> Preflight(Guid id, SshTarget target, CancellationToken ct) => WithNode(id,
        node => deployment.Preflight(node, target, Developer, ct));

    [HttpPost("nodes/{id:guid}/deploy")]
    [ApiCapability("managed.deployment.run", "Передать пакет и запустить узел", "management", RequiresConfirmation = true)]
    public Task<IActionResult> Deploy(Guid id, SshTarget target, CancellationToken ct) => WithNode(id,
        node => Task.FromResult(deployment.Enqueue(node, target, Developer, Login)));

    [HttpGet("jobs")]
    public IActionResult Jobs() => Ok(deployment.Jobs().Where(x =>
        network.Find(x.NodeId) is { } node && network.CanManage(node, Actor(), MayManageAsDeveloper(node.Id))));

    private Task<IActionResult> WithNode<T>(Guid id, Func<ManagedNode, Task<T>> action)
    {
        var node = network.Find(id);
        return node is { Kind: "global" } && Developer && Parent() is null
            ? Run(() => action(node)) : Task.FromResult<IActionResult>(Forbid());
    }

    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (HttpRequestException) { return StatusCode(502, new { error = "Не удалось выполнить запрос к Auth. Проверьте журналы сервера." }); }
    }

    private Guid? Parent(string? kind = null)
    {
        var principal = peers.Validate(Request.Headers[ParentAuthorization.Header].ToString());
        return principal is not null && (kind is null || principal.IsInRole(kind)) &&
            Guid.TryParse(principal.FindFirstValue("identity_id"), out var id) ? id : null;
    }

    public sealed record ApplicationInput(string Name, string Description, string? Address = null, string? Contact = null, string? Website = null);
    public sealed record HomeInput(string Machine);
    public sealed record DecisionInput(bool Approved, string? Note);
    public sealed record MemberInput(bool Allowed);
}


[ApiController, Authorize(Policy = "node-peer"), Route("api/v1/managed/club/enroll")]
public sealed class ClubEnrollmentController(ManagedNetwork network) : ControllerBase
{
    [HttpPost, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> Enroll(ClubJoin input, CancellationToken ct)
    {
        if (!User.IsInRole("local") || !Guid.TryParse(User.FindFirstValue("identity_id"), out var local)) return Forbid();
        try { return Ok(await network.EnrollAgent(local, input, ct)); }
        catch (NodeApiException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
    }
}

[ApiController, Authorize(Policy = "node-peer"), Route("api/v1/managed/peers")]
public sealed class ManagedPeerController(ManagedNetwork network) : ControllerBase
{
    [HttpGet("{parent:guid}/{agent:guid}")]
    public IActionResult Verify(Guid parent, Guid agent)
    {
        if (!Guid.TryParse(User.FindFirstValue("identity_id"), out var device) || device != parent) return Forbid();
        var owner = network.Find(parent);
        var target = network.Find(agent);
        if (owner is not { Enabled: true } || target is not { Kind: "agent", Enabled: true }) return Forbid();
        var valid = owner.Kind switch
        {
            "local" => User.IsInRole("local") && target.ParentId == owner.Id,
            "global" => User.IsInRole("global") && network.Organization(target)?.Id == owner.Id,
            _ => false
        };
        return valid ? NoContent() : Forbid();
    }
}

[ApiController, Authorize(Policy = "node-peer"), Route("api/v1/managed/configuration")]
public sealed class DeviceConfigurationController(ManagedNetwork network) : ControllerBase
{
    [HttpGet, ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Read(CancellationToken ct)
    {
        if (!Guid.TryParse(User.FindFirstValue("identity_id"), out var device)) return Forbid();
        try { return Ok(await network.RefreshDevice(device, ct)); }
        catch (NodeApiException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
    }
}
