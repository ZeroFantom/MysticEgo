using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Avatar;

namespace MysticEgo.Api.Global.Controllers;

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
    [HttpGet("organization")]
    public IActionResult Organization() => Admin()
        ? Ok(new { theme = catalog.Theme, assets = catalog.List("organization", configuration["Avatar:OrganizationId"] ?? "default") }) : Forbid();

    [Authorize]
    [HttpPost("organization/assets")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadOrganization(CancellationToken ct)
    {
        if (!Admin()) return Forbid();
        if (!Request.HasFormContentType) return BadRequest("multipart/form-data required");
        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return BadRequest("file required");
        try
        {
            using var stream = file.OpenReadStream();
            return Ok(await catalog.AddAsync("organization", configuration["Avatar:OrganizationId"] ?? "default", form["kind"].ToString(), file.FileName, stream, ct));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidDataException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize]
    [HttpPut("organization/theme")]
    public async Task<IActionResult> Theme([FromBody] ThemePayload request, CancellationToken ct)
    {
        if (!Admin()) return Forbid();
        try { return Ok(await catalog.PublishAsync(configuration["Avatar:OrganizationId"] ?? "default", request, ct)); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize]
    [HttpGet("assets/{id}")]
    public IActionResult GetAsset(string id)
    {
        var asset = catalog.GetAsset(id);
        if (asset is null) return NotFound();
        if (!Admin() || asset.Scope != "organization" || asset.Owner != (configuration["Avatar:OrganizationId"] ?? "default")) return Forbid();
        var path = catalog.AssetPath(id);
        return path is null ? NotFound() : PhysicalFile(path, AvatarRegistration.ContentType(asset.Extension));
    }

    [HttpGet("replica/manifest")]
    public IActionResult ReplicaManifest()
    {
        if (!ReplicaKey()) return StatusCode(403);
        var theme = catalog.Theme;
        if (theme is null) return NotFound();
        var assets = new[] { theme.ModelId }.Concat(theme.AnimationIds).Select(catalog.GetAsset).OfType<AvatarAsset>().ToArray();
        return Ok(new ThemeReplicaManifest(theme, assets));
    }

    [HttpGet("replica/assets/{id}")]
    public IActionResult ReplicaAsset(string id)
    {
        if (!ReplicaKey()) return StatusCode(403);
        var asset = catalog.GetAsset(id);
        var theme = catalog.Theme;
        if (asset is null || theme is null || asset.Owner != theme.OrganizationId ||
            !(theme.ModelId == id || theme.AnimationIds.Contains(id))) return NotFound();
        var path = catalog.AssetPath(id);
        return path is null ? NotFound() : PhysicalFile(path, "application/octet-stream");
    }

}
