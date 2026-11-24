using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Avatar;

namespace MysticEgo.Api.Local.Controllers;

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

    [HttpGet("replica/manifest")]
    public IActionResult ReplicaManifest() => ReplicaKey() ? ReplicaFiles.ReadManifest(configuration).ToActionResult() : StatusCode(403);

    [HttpGet("replica/assets/{id}")]
    public IActionResult ReplicaAsset(string id) => ReplicaKey() ? ReplicaFiles.GetAsset(configuration, id).ToActionResult() : StatusCode(403);

    [Authorize]
    [HttpGet("theme")]
    public IActionResult Theme() => User.Identity?.IsAuthenticated == true ? ReplicaFiles.ReadManifest(configuration).ToActionResult() : Forbid();

    [Authorize]
    [HttpGet("theme/assets/{id}")]
    public IActionResult ThemeAsset(string id) => User.Identity?.IsAuthenticated == true ? ReplicaFiles.GetAsset(configuration, id).ToActionResult() : Forbid();

}
