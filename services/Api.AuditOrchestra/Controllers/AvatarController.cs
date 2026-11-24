using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Avatar;

namespace MysticEgo.Api.AuditOrchestra.Controllers;

[ApiController]
[Route("api/v1/avatars")]
public sealed class AvatarController(AvatarCatalog catalog, IAvatarAccessPolicy access, IConfiguration configuration) : ControllerBase
{
    private string? UserId() => access.HumanId(User);
    private bool Admin() => access.IsAdmin(User);
    private bool ReplicaKey() => access.ValidReplicaKey(Request.Headers);

    [Authorize]
    [HttpGet("providers")]
    public IActionResult Providers() => User.Identity?.IsAuthenticated == true ? Ok(AvatarProviders.List()) : Forbid();

    [Authorize]
    [HttpGet("personal")]
    public IActionResult Personal() => UserId() is { } id
        ? Ok(new { selection = catalog.GetPersonal(id), assets = catalog.List("personal", id) }) : Forbid();

    [Authorize]
    [HttpPost("personal/assets")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadPersonal(CancellationToken ct)
    {
        if (UserId() is not { } id) return Forbid();
        if (!Request.HasFormContentType) return BadRequest("multipart/form-data required");
        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return BadRequest("file required");
        try
        {
            using var stream = file.OpenReadStream();
            return Ok(await catalog.AddAsync("personal", id, form["kind"].ToString(), file.FileName, stream, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize]
    [HttpPut("personal/selection")]
    public async Task<IActionResult> Selection([FromBody] SetAvatar request, CancellationToken ct)
    {
        if (UserId() is not { } id) return Forbid();
        try { return Ok(await catalog.SetPersonalAsync(id, request.AssetId, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize]
    [HttpGet("assets/{id}")]
    public IActionResult GetAsset(string id)
    {
        var asset = catalog.GetAsset(id);
        if (asset is null) return NotFound();
        if (UserId() is not { } uid || asset.Scope != "personal" || asset.Owner != uid) return Forbid();
        var path = catalog.AssetPath(id);
        return path is null ? NotFound() : PhysicalFile(path, AvatarRegistration.ContentType(asset.Extension));
    }

}
