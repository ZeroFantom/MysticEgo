using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/managed/nodes/{id:guid}/timeweb")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class GlobalTimewebController(ManagedNetwork network, DevelopmentTeam team, IConfiguration settings,
    IHttpClientFactory clients, NodeState state) : ControllerBase
{
    private static readonly ConcurrentDictionary<int, SemaphoreSlim> Operations = new();

    [HttpGet]
    public Task<IActionResult> Status(Guid id, CancellationToken ct) => Execute(id, null, ct);

    [HttpPost("power")]
    public Task<IActionResult> Power(Guid id, PowerRequest input, CancellationToken ct) =>
        input.Confirmed && input.Action is ("start" or "shutdown" or "reboot")
            ? Execute(id, input.Action, ct) : Task.FromResult<IActionResult>(BadRequest(new { error = "Подтвердите допустимое действие." }));

    private async Task<IActionResult> Execute(Guid id, string? action, CancellationToken ct)
    {
        if (!team.IsOwner(User) && !await team.IsMember(User, ct)) return Forbid();
        if (network.Find(id) is not { Kind: "global", Enabled: true } node || id == network.RootId) return NotFound();
        var config = settings.GetSection("Timeweb:Globals:" + id.ToString("D"));
        var token = settings["Timeweb:Token"];
        if (!int.TryParse(config["ServerId"], out var server) || server <= 0 ||
            !int.TryParse(settings["Timeweb:OrchestraServerId"], out var orchestra) || orchestra <= 0 ||
            string.IsNullOrWhiteSpace(token) || !IPAddress.TryParse(config["Ipv4"], out var ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork)
            return Conflict(new { error = "Главный разработчик ещё не настроил привязку Global к Timeweb." });
        if (server == orchestra) return Conflict(new { error = "VPS самой Orchestra исключён из управления Global." });

        var gate = Operations.GetOrAdd(server, _ => new SemaphoreSlim(1,1));
        if (!await gate.WaitAsync(0, ct)) return Conflict(new { error = "На этом VPS уже выполняется запрос." });
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var client = clients.CreateClient("mysticego-timeweb");
            var ips = await Get(client, $"servers/{server}/ips", token, timeout.Token);
            if (ips?["server_ips"] is not JsonArray list ||
                !list.OfType<JsonObject>().Any(x => Value(x,"type") == "ipv4" && Value(x,"ip") == ip.ToString()))
                return Conflict(new { error = "IPv4 не принадлежит указанному VPS Timeweb." });
            if (!Uri.TryCreate(node.PublicUrl, UriKind.Absolute, out var url) ||
                !(await Dns.GetHostAddressesAsync(url.Host, timeout.Token)).Contains(ip))
                return Conflict(new { error = "Адрес зарегистрированного Global не совпадает с привязкой." });

            var reply = await Get(client, $"servers/{server}", token, timeout.Token);
            if (reply?["server"] is not JsonObject details || Value(details,"id") != server.ToString())
                return StatusCode(502, new { error = "Timeweb вернул неожиданный идентификатор VPS." });
            var status = Value(details,"status");
            if (action is null) return Ok(new { serverId = server, ipv4 = ip.ToString(), status });
            if (action == "start" ? status != "off" : status != "on")
                return Conflict(new { error = "Состояние VPS изменилось. Обновите статус перед повторной попыткой." });
            using var result = await Send(client, HttpMethod.Post, $"servers/{server}/{action}", token, timeout.Token);
            result.EnsureSuccessStatusCode();
            state.Log(User.Identity?.Name ?? "developer", "timeweb.global." + action, id.ToString(), true, server.ToString());
            return Accepted(new { serverId = server, action });
        }
        catch (HttpRequestException)
        {
            return StatusCode(502, new { error = "Не удалось подтвердить ответ Timeweb. Проверьте состояние VPS перед повтором." });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return StatusCode(504, new { error = "Timeweb не ответил вовремя. Проверьте состояние VPS перед повтором." });
        }
        finally { gate.Release(); }
    }

    private static string Value(JsonObject item, string key) =>
        item[key] is JsonValue value ? value.ToString() : "";

    private static async Task<JsonObject?> Get(HttpClient client, string path, string token, CancellationToken ct)
    {
        using var response = await Send(client, HttpMethod.Get, path, token, ct);
        response.EnsureSuccessStatusCode();
        return JsonNode.Parse(await response.Content.ReadAsStringAsync(ct)) as JsonObject;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, "https://api.timeweb.cloud/api/v1/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request, ct);
    }

    public sealed record PowerRequest(string Action, bool Confirmed);
}
