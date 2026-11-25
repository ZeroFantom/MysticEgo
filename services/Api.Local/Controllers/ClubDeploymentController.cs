using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Local.Controllers;

[ApiController, Route("api/v1")]
public sealed class ClubDeploymentController(NodeOptions node, IdentityTokenSource identity, IHttpClientFactory clients,
    IConfiguration config, INodeAccessPolicy access, NodeState state) : ControllerBase
{
    [AllowAnonymous, HttpGet("public/club")]
    public IActionResult Club() => node.Managed && Guid.TryParse(node.GlobalId, out var global) ?
        Ok(new { localId = node.IdentityId, globalId = global, name = node.DisplayName, address = node.ClubAddress }) : NotFound();

    [AllowAnonymous, HttpPost("public/club/enroll"), RequestSizeLimit(8192)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Enroll(ClubJoin input, CancellationToken ct)
    {
        if (!node.Managed || !ClubLan.PrivateAddress(HttpContext.Connection.RemoteIpAddress)) return Forbid();
        using var client = clients.CreateClient("mysticego-logged-api");
        using var request = new HttpRequestMessage(HttpMethod.Post, node.OrchestraUrl!.TrimEnd('/') + "/api/v1/managed/club/enroll")
        {
            Content = JsonContent.Create(input)
        };
        request.Headers.Authorization = new("Bearer", await identity.GetAsync(ct));
        using var response = await client.SendAsync(request, ct);
        return new ContentResult { Content = await response.Content.ReadAsStringAsync(ct), StatusCode = (int)response.StatusCode, ContentType = "application/json" };
    }

    [Authorize, HttpPost("club/distribution/{platform}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Download(string platform, CancellationToken ct)
    {
        if (!access.Admin(User) || !node.Managed) return Forbid();
        if (platform is not ("windows" or "linux" or "assignment")) return BadRequest();
        var root = config["Managed:PackageRoot"];
        var launcher = platform == "windows" ? "AgentDesktop.cmd" : "agent-desktop";
        var executable = platform == "windows" ? "runtime/electron/electron.exe" : "runtime/electron-linux/electron";
        if (platform != "assignment" && (root is null ||
            !System.IO.File.Exists(Path.Combine(root, launcher)) || !System.IO.File.Exists(Path.Combine(root, executable))))
            return Conflict(new { error = "Установите полный комплект MysticEgo на сервере Local или скачайте конфигурацию отдельно." });
        using var client = clients.CreateClient("mysticego-logged-api");
        using var request = new HttpRequestMessage(HttpMethod.Post, node.OrchestraUrl!.TrimEnd('/') + "/api/v1/managed/club/assignment");
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(Request.Headers.Authorization.ToString());
        request.Headers.TryAddWithoutValidation("X-Mystic-Parent", await identity.GetAsync(ct));
        using var response = await client.SendAsync(request, ct);
        var assignment = await response.Content.ReadAsByteArrayAsync(ct);
        if (!response.IsSuccessStatusCode)
            return new ContentResult { Content = System.Text.Encoding.UTF8.GetString(assignment), StatusCode = (int)response.StatusCode, ContentType = "application/json" };
        if (platform == "assignment") return File(assignment, "application/json", "club-assignment.json");

        var temp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var output = System.IO.File.Create(temp))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                await using (var entry = archive.CreateEntry("club-assignment.json").Open()) await entry.WriteAsync(assignment, ct);
                var includes = new List<string> { "desktop/station", "desktop/control/center.mjs", "desktop/control/style.css", "desktop/control/effects.js",
                    "desktop/launch-runtime.mjs", "desktop/main.mjs", "desktop/preload.cjs", "desktop/package.json", "desktop/dist",
                    "runtime/app/bin", "runtime/app/settings", "runtime/app/web/agent", "runtime/app/web/audit", "runtime/vision", "runtime/avatars" };
                if (platform == "windows") includes.AddRange(["AgentDesktop.cmd", "runtime/electron", "runtime/win-x64"]);
                else includes.AddRange(["agent-desktop", "desktop/launch-linux.sh", "runtime/electron-linux", "runtime/linux-x64.tar.gz"]);
                foreach (var include in includes)
                {
                    var path = Path.Combine(root!, include);
                    if (System.IO.File.Exists(path)) await Add(path);
                    else if (Directory.Exists(path))
                        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) await Add(file);
                }

                async Task Add(string file)
                {
                    var relative = Path.GetRelativePath(root!, file).Replace('\\', '/');
                    var entry = archive.CreateEntry(relative, relative.EndsWith(".gz") ? CompressionLevel.NoCompression : CompressionLevel.Fastest);
                    if (!OperatingSystem.IsWindows()) entry.ExternalAttributes = ((int)System.IO.File.GetUnixFileMode(file) & 511) << 16;
                    else if (relative is "agent-desktop" or "desktop/launch-linux.sh" || relative.StartsWith("runtime/electron-linux/"))
                        entry.ExternalAttributes = 0x81ed << 16;
                    await using var from = System.IO.File.OpenRead(file);
                    await using var to = entry.Open();
                    await from.CopyToAsync(to, ct);
                }
            }
            var fileStream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 65_536, FileOptions.DeleteOnClose);
            state.Log(User.Identity?.Name ?? "operator", "club.agent.package", node.NodeId, true, platform);
            return File(fileStream, "application/zip", $"MysticEgo-Agent-{platform}.zip");
        }
        catch { if (System.IO.File.Exists(temp)) System.IO.File.Delete(temp); throw; }
    }
}

