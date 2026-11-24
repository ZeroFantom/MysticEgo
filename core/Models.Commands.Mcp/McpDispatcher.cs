using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Commands.SignalR.Hub;

namespace Models.Commands.Mcp;

public sealed class McpDispatcher : IMcpDispatcher
{
    private readonly ICapabilityStore _capabilities;
    private readonly IMcpConverter _converter;
    private readonly IMcpToolExecutor _executor;
    private readonly ILogger<McpDispatcher> _log;

    public McpDispatcher(
        ICapabilityStore capabilities,
        IMcpConverter converter,
        IMcpToolExecutor executor,
        ILogger<McpDispatcher>? log = null)
    {
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _log = log ?? NullLogger<McpDispatcher>.Instance;
    }

    public async Task<JsonRpcResponse> DispatchAsync(
        JsonRpcRequest request, CancellationToken ct = default)
    {
        var response = new JsonRpcResponse { Id = request?.Id };

        if (request is null || string.IsNullOrWhiteSpace(request.Method))
        {
            response.Error = new JsonRpcError
            {
                Code = JsonRpcErrorCodes.InvalidRequest,
                Message = "Invalid JSON-RPC request.",
            };
            return response;
        }

        try
        {
            switch (request.Method)
            {
                case "initialize":
                    response.Result = BuildInitialize();
                    break;

                case "ping":
                    response.Result = new { };
                    break;

                case "tools/list":
                    response.Result = await ListToolsAsync(ct).ConfigureAwait(false);
                    break;

                case "tools/call":
                    response.Result = await CallToolAsync(request.Params, ct)
                        .ConfigureAwait(false);
                    break;

                case "resources/list":
                    response.Result = new McpListResourcesResult();
                    break;

                case "resources/read":
                    response.Result = new McpReadResourceResult();
                    break;

                case "notifications/initialized":
                case "notifications/cancelled":
                    response.Result = new { };
                    break;

                default:
                    response.Error = new JsonRpcError
                    {
                        Code = JsonRpcErrorCodes.MethodNotFound,
                        Message = $"Unknown MCP method: '{request.Method}'.",
                    };
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "MCP dispatcher failure для '{Method}'", request.Method);

            response.Error = new JsonRpcError
            {
                Code = JsonRpcErrorCodes.InternalError,
                Message = ex.Message,
            };
        }

        return response;
    }

    // ── Methods ────────────────────────────────────────

    private McpInitializeResult BuildInitialize() => new()
    {
        ProtocolVersion = "2024-11-05",
        ServerInfo = new McpServerInfo
        {
            Name = "MysticEgo.Mcp",
            Version = typeof(McpDispatcher).Assembly.GetName().Version?.ToString()
                      ?? "1.0.0",
        },
        Capabilities = new McpServerCapabilities { Tools = new { } },
    };

    private async Task<McpListToolsResult> ListToolsAsync(CancellationToken ct)
    {
        var capabilities = await _capabilities
            .GetAllAsync(ct)
            .ConfigureAwait(false);

        var tools = _converter.ToTools(capabilities).ToList();

        _log.LogDebug("tools/list → {Count} инструментов", tools.Count);

        return new McpListToolsResult { Tools = tools };
    }

    private async Task<McpCallToolResult> CallToolAsync(
        JsonElement? args, CancellationToken ct)
    {
        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
            return McpCallToolResult.Fail("tools/call: params missing");

        if (!args.Value.TryGetProperty("name", out var nameEl) ||
            nameEl.ValueKind != JsonValueKind.String)
            return McpCallToolResult.Fail("tools/call: 'name' is required");

        var name = nameEl.GetString() ?? "";

        JsonElement? toolArgs = null;
        if (args.Value.TryGetProperty("arguments", out var a) &&
            a.ValueKind == JsonValueKind.Object)
            toolArgs = a;

        return await _executor.ExecuteAsync(name, toolArgs, ct).ConfigureAwait(false);
    }
}