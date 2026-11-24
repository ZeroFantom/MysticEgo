using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Avatar;

public static class PersonalMotionCatalog
{
    public static readonly string[] Slots = ["idle", "walk", "wave", "nod", "dance", "talk", "sit", "lean", "perch", "bump", "listen"];
    public static object Build(string style)
    {
        var intensity = style switch { "calm" => 0.5, "lively" => 1.1, "focused" => 0.75, _ => throw new ArgumentException("Выберите стиль пакета.") };
        return new
        {
            version = "mysticego-motion-1", style, intensity,
            clips = Slots.Select(value => new
            {
                motion = value,
                duration = value is "wave" or "bump" ? 1.1 : value is "dance" or "walk" ? 1.8 : 3.5,
                loop = value is not ("wave" or "bump"),
                engine = "mysticego.procedural"
            })
        };
    }
}

[ApiController, Authorize, Route("api/v1/avatars/personal"), Route("api/v1/avatars/account")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AvatarPersonalController(AvatarCatalog catalog, IAvatarAccessPolicy access) : ControllerBase
{
    private string? Actor => access.HumanId(User);
    [HttpGet("profile")]
    public IActionResult Profile() => Actor is { } id ?
        Ok(new { selection = catalog.GetPersonal(id), assets = catalog.List("personal", id), slots = PersonalMotionCatalog.Slots }) : Forbid();

    [HttpPost("asset"), RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        if (Actor is not { } id) return Forbid();
        if (!Request.HasFormContentType) return BadRequest();
        var form = await Request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null) return BadRequest();
        try
        {
            await using var stream = file.OpenReadStream();
            return Ok(await catalog.AddAsync("personal", id, form["kind"].ToString(), file.FileName, stream, ct));
        }
        catch (ArgumentException error) { return BadRequest(new { error = error.Message }); }
    }

    [HttpGet("asset/{id}")]
    public IActionResult Asset(string id)
    {
        var asset = catalog.GetAsset(id);
        if (Actor is not { } actor || asset is not { Scope: "personal" } || asset.Owner != actor) return NotFound();
        var file = catalog.AssetPath(id);
        return file is not null ? PhysicalFile(file, AvatarRegistration.ContentType(asset.Extension)) : NotFound();
    }

    [HttpPut("motion-pack")]
    public async Task<IActionResult> Pack(AvatarPack input, CancellationToken ct)
    {
        if (Actor is not { } id) return Forbid();
        try { return Ok(await catalog.SetPersonalAsync(id, input.AssetId, input.Motions, ct, input.Style, input.Appearance)); }
        catch (ArgumentException error) { return BadRequest(new { error = error.Message }); }
    }

    [HttpPost("motion-pack/generate")]
    public IActionResult Generate(GenerateMotions input)
    {
        if (Actor is null) return Forbid();
        try { return Ok(PersonalMotionCatalog.Build(input.Style)); }
        catch (ArgumentException error) { return BadRequest(new { error = error.Message }); }
    }

    public sealed record AvatarPack(string AssetId, Dictionary<string, string>? Motions, string Style = "calm", AvatarAppearance? Appearance = null);
    public sealed record GenerateMotions(string Style);
}
