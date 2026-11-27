using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Models.Commands.Mcp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

// Explicitly local bridge. No device tokens, cluster forwarding or caller-provided upstream.
public sealed class UiAutomationBridge(IConfiguration configuration, IHttpClientFactory clients)
{
    public bool IsEnabled => configuration.GetValue<bool>("UiAutomation:Enabled");
    public string? Owner => configuration["UiAutomation:Owner"] ??
        Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");

    private string? Token => configuration["UiAutomation:LocalToken"] ??
        Environment.GetEnvironmentVariable("MYSTICEGO_UI_TOKEN");

    public async Task<McpListToolsResult?> ToolsAsync(CancellationToken ct)
    {
        try
        {
            using var response = await SendLocalAsync("mcp", new { jsonrpc = "2.0", id = 1, method = "tools/list" }, ct);
            if (response is null || !response.IsSuccessStatusCode) return null;
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return json.RootElement.GetProperty("result").Deserialize<McpListToolsResult>();
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException ||
            e is OperationCanceledException && !ct.IsCancellationRequested) { return null; }
    }

    public async Task<McpCallToolResult> CallAsync(string name, JsonElement args, CancellationToken ct)
    {
        if (!UiAutomationController.IsOperation(name)) return McpCallToolResult.Fail("Unknown UI tool.");
        try
        {
            object body = args.ValueKind == JsonValueKind.Undefined ? new { } : args;
            using var response = await SendLocalAsync("api/v1/ui/" + name, body, ct);
            if (response is null || !response.IsSuccessStatusCode) return McpCallToolResult.Fail("Local UI executor unavailable or operation denied.");
            var text = await response.Content.ReadAsStringAsync(ct);
            using var json = JsonDocument.Parse(text);
            if (json.RootElement.TryGetProperty("image", out var image) && image.ValueKind == JsonValueKind.String)
                return new McpCallToolResult { Content = { new McpContentBlock { Type = "image", Text = null, Data = image.GetString(), MimeType = "image/png" } } };
            return McpCallToolResult.Ok(text);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException ||
            e is OperationCanceledException && !ct.IsCancellationRequested)
        { return McpCallToolResult.Fail("Local UI operation failed."); }
    }

    private async Task<HttpResponseMessage?> SendLocalAsync(string path, object body, CancellationToken ct)
    {
        var token = Token;
        if (!IsEnabled || string.IsNullOrWhiteSpace(token) || token.Length < 32) return null;
        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:1247/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        request.Content = JsonContent.Create(body);
        return await clients.CreateClient("mysticego-local-ui").SendAsync(request, ct);
    }

    public async Task ForwardAsync(HttpContext context, string path, CancellationToken cancellation)
    {
        var token = configuration["UiAutomation:LocalToken"] ??
            Environment.GetEnvironmentVariable("MYSTICEGO_UI_TOKEN");
        if (string.IsNullOrWhiteSpace(token) || token.Length < 32)
        { context.Response.StatusCode = 503; return; }

        using var request = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:1247/" + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");
        if (context.Request.Headers.TryGetValue("MCP-Protocol-Version", out var version))
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", version.ToString());
        request.Content = new StreamContent(context.Request.Body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        try
        {
            using var response = await clients.CreateClient("mysticego-local-ui").SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellation);
            context.Response.StatusCode = (int)response.StatusCode;
            if (response.Content.Headers.ContentType is { } contentType)
                context.Response.ContentType = contentType.ToString();
            context.Response.Headers.CacheControl = "no-store";
            await response.Content.CopyToAsync(context.Response.Body, cancellation);
        }
        catch (HttpRequestException) when (!context.Response.HasStarted)
        { context.Response.StatusCode = 503; }
        catch (OperationCanceledException) when (!context.Response.HasStarted && !cancellation.IsCancellationRequested)
        { context.Response.StatusCode = 504; }
    }
}

[ApiController, Authorize]
[Route("api/v1/managed/ui-automation")]
public sealed class UiAutomationController(NodeOptions node, UiAutomationBridge bridge, IAssistantAccessPolicy assistant) : ControllerBase
{
    private static readonly HashSet<string> Operations = new(StringComparer.Ordinal)
    {
        "ui_targets", "ui_open", "ui_snapshot", "ui_screenshot", "ui_navigate",
        "ui_click", "ui_fill", "ui_scroll", "ui_assert_visible", "ui_close",
        "ui_record_start", "ui_record_stop", "ui_replay",
        "ui_scenario_export", "ui_scenario_import", "ui_replay_cancel",
        "ui_session_status", "ui_library_list", "ui_library_load",
        "native_targets", "native_attach", "native_snapshot", "native_click", "native_key",
        "native_pointer_get", "native_pointer_set", "native_close", "native_vendor_launch", "native_replay_clicks"
    };

    internal static bool IsOperation(string name) => Operations.Contains(name);

    private bool Allowed() =>
        node.Kind is NodeKind.Agent or NodeKind.AuditOrchestra &&
        bridge.IsEnabled && !string.IsNullOrWhiteSpace(bridge.Owner) &&
        User.Identity?.IsAuthenticated == true &&
        User.FindFirst("token_use")?.Value != "device" &&
        string.Equals(assistant.ResolveOwner(User), bridge.Owner, StringComparison.Ordinal) &&
        HttpContext.Connection.RemoteIpAddress is { } ip && IPAddress.IsLoopback(ip) &&
        !Request.Headers.ContainsKey("Forwarded") &&
        !Request.Headers.ContainsKey("X-Forwarded-For") &&
        !Request.Headers.ContainsKey("Origin");

    [HttpPost("mcp"), RequestSizeLimit(65536)]
    public async Task Mcp(CancellationToken cancellation)
    {
        if (!Allowed()) { Response.StatusCode = 403; return; }
        await bridge.ForwardAsync(HttpContext, "mcp", cancellation);
    }

    [HttpPost("actions/{operation}"), RequestSizeLimit(65536)]
    public async Task ActionCall(string operation, CancellationToken cancellation)
    {
        if (!Allowed()) { Response.StatusCode = 403; return; }
        if (!Operations.Contains(operation)) { Response.StatusCode = 404; return; }
        await bridge.ForwardAsync(HttpContext, "api/v1/ui/" + operation, cancellation);
    }
}

public static class UiAutomationRegistration
{
    public static IServiceCollection AddUiAutomationBridge(this IServiceCollection services)
    {
        services.AddSingleton<UiAutomationBridge>();
        services.AddHttpClient("mysticego-local-ui", client => client.Timeout = TimeSpan.FromSeconds(120))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            { UseProxy = false, UseCookies = false, AllowAutoRedirect = false });
        return services;
    }
}
