using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Autorization.Attributes;

namespace Api.Auth.Controllers;

// Personal presentation preferences only. Never store credentials, device permissions or arbitrary CSS.
public sealed class AccountPresentationStore(IConfiguration config, IWebHostEnvironment environment)
{
    private readonly SemaphoreSlim gate = new(1);
    private string Root => Path.GetFullPath(config["Presentation:Directory"] ?? Path.Combine(environment.ContentRootPath, "data", "presentation"));
    private static readonly Dictionary<string, string[]> Choices = new()
    {
        ["language"] = ["en","zh-CN","ko","ru"], ["font"] = ["modern","editorial","system","mono"],
        ["theme"] = ["dark","light","system"], ["interfaceStyle"] = ["simplified","immersive"],
        ["transition"] = ["flow","fade","slide","scale"], ["uiMotion"] = ["off","low","normal"],
        ["density"] = ["comfortable","compact"], ["size"] = ["normal","large"],
        ["palette"] = ["iris","jade","amber","arctic","rose","lime","indigo","coral","steel","custom"]
    };
    public static bool Valid(JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Object) return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in patch.EnumerateObject())
        {
            if (!seen.Add(property.Name)) return false;
            var v = property.Value;
            if (Choices.TryGetValue(property.Name, out var choices))
            { if (v.ValueKind != JsonValueKind.String || !choices.Contains(v.GetString())) return false; }
            else if (property.Name is "ambient" or "wave" or "grid" or "border")
            { if (v.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return false; }
            else if (property.Name is "primary" or "secondary")
            { if (v.ValueKind != JsonValueKind.String || !System.Text.RegularExpressions.Regex.IsMatch(v.GetString()!, @"\A#[0-9a-fA-F]{6}\z")) return false; }
            else if (property.Name == "intensity")
            { if (v.ValueKind != JsonValueKind.Number || !v.TryGetInt32(out var n) || n < 10 || n > 100) return false; }
            else return false;
        }
        return seen.Count > 0;
    }
    public async Task<Dictionary<string,JsonElement>> ReadOrPatch(Guid user, JsonElement? patch, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var path = Path.Combine(Root, user.ToString("N") + ".json");
            var data = File.Exists(path)
                ? JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(await File.ReadAllTextAsync(path,ct)) ?? new()
                : new Dictionary<string,JsonElement>();
            if (patch is { } changes)
            {
                foreach (var property in changes.EnumerateObject()) data[property.Name] = property.Value.Clone();
                Directory.CreateDirectory(Root);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(data),ct);
                    File.Move(temp,path,true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            return data;
        }
        finally { gate.Release(); }
    }
}

[ApiController, Authorize, SkipPermission]
[Route("api/auth/presentation")]
public sealed class AccountPresentationController(AccountPresentationStore store) : BaseApiController
{
    private Guid? Owner => User.FindFirst("token_use")?.Value == "device" ? null : CurrentUserId;
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (Owner is not Guid uid) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        return Ok(await store.ReadOrPatch(uid,null,ct));
    }
    [HttpPost, RequestSizeLimit(4096)]
    public async Task<IActionResult> Save([FromBody] JsonElement patch,CancellationToken ct)
    {
        if (Owner is not Guid uid) return Unauthorized();
        if (!AccountPresentationStore.Valid(patch)) return BadRequest(new { code="invalid_presentation" });
        Response.Headers.CacheControl = "no-store";
        return Ok(await store.ReadOrPatch(uid,patch,ct));
    }
}
