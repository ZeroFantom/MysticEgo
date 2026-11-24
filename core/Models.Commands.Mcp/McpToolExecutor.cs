using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Commands.SignalR;
using Models.Commands.SignalR.Hub;

namespace Models.Commands.Mcp;

public sealed class McpToolExecutor : IMcpToolExecutor
{
    private readonly IExecutionDispatcher _dispatcher;
    private readonly ICapabilityStore _capabilities;
    private readonly ILogger<McpToolExecutor> _log;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public McpToolExecutor(
        IExecutionDispatcher dispatcher,
        ICapabilityStore capabilities,
        ILogger<McpToolExecutor>? log = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        _log = log ?? NullLogger<McpToolExecutor>.Instance;
    }

    public async Task<McpCallToolResult> ExecuteAsync(
        string toolName,
        JsonElement? arguments,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
            return McpCallToolResult.Fail("tool name is required");

        if (!CommandId.TryParse(toolName, out var commandId))
            return McpCallToolResult.Fail($"invalid tool name '{toolName}'");

        // Проверяем, что capability существует у какого-нибудь клиента
        var capability = await FindCapabilityAsync(commandId, ct).ConfigureAwait(false);
        if (capability is null)
        {
            _log.LogInformation(
                "tools/call: capability '{Id}' не найдена ни у одного клиента",
                toolName);

            return McpCallToolResult.Fail(
                $"No online client supports '{toolName}'.");
        }

        var namedArgs = ExtractArguments(arguments);
        var timeout = ResolveTimeout(capability);

        var request = new CommandExecutionRequest
        {
            ExecutionId = Guid.NewGuid(),
            CommandId = commandId,
            NamedArgs = namedArgs.Count == 0 ? null : namedArgs,
            RawArgs = "",
            IssuedAt = DateTime.UtcNow,
        };

        var sw = Stopwatch.StartNew();

        CommandExecutionResult result;
        try
        {
            result = await _dispatcher
                .DispatchToAnyAsync(request, timeout, progress: null, ct)
                .ConfigureAwait(false);
        }
        catch (ExecutionRoutingException ex)
        {
            sw.Stop();
            _log.LogInformation(
                "tools/call '{Id}': нет онлайн-клиента ({Reason})",
                toolName, ex.Message);

            return McpCallToolResult.Fail(
                $"No online client supports '{toolName}'.");
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            return McpCallToolResult.Fail(
                $"Tool '{toolName}' cancelled by client.");
        }
        catch (Exception ex)
        {
            sw.Stop();
            _log.LogWarning(ex,
                "tools/call '{Id}' упал в dispatcher", toolName);

            return McpCallToolResult.Fail(
                $"Tool '{toolName}' failed: {ex.Message}");
        }

        sw.Stop();

        _log.LogInformation(
            "tools/call '{Id}': success={Ok} duration={Ms}мс exec={Exec}",
            toolName, result.Success, sw.ElapsedMilliseconds, result.ExecutionId);

        if (!result.Success)
        {
            var err = string.IsNullOrWhiteSpace(result.ErrorMessage)
                ? "Command failed."
                : result.ErrorMessage;

            if (!string.IsNullOrWhiteSpace(result.ErrorCode))
                err = $"[{result.ErrorCode}] {err}";

            return McpCallToolResult.Fail(err);
        }

        var output = string.IsNullOrWhiteSpace(result.Output)
            ? "(no output)"
            : result.Output;

        return McpCallToolResult.Ok(output);
    }

    // ── Helpers ────────────────────────────────────────

    private async Task<CapabilityDto?> FindCapabilityAsync(
        CommandId commandId, CancellationToken ct)
    {
        // Ищем любого клиента, у которого есть эта capability
        var client = await _capabilities
            .FindClientAsync(commandId, ct)
            .ConfigureAwait(false);

        if (client is null) return null;

        // Возвращаем саму capability для чтения метаданных (timeout)
        var caps = await _capabilities
            .GetAsync(client.ClientId, ct)
            .ConfigureAwait(false);

        return caps.FirstOrDefault(c => c.Id == commandId);
    }

    private static Dictionary<string, object?> ExtractArguments(JsonElement? args)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (args is null || args.Value.ValueKind != JsonValueKind.Object)
            return result;

        foreach (var prop in args.Value.EnumerateObject())
        {
            result[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.TryGetInt64(out var l)
                    ? l
                    : prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Array or JsonValueKind.Object => prop.Value.GetRawText(),
                _ => prop.Value.GetRawText(),
            };
        }

        return result;
    }

    private static TimeSpan ResolveTimeout(CapabilityDto capability)
        => DefaultTimeout;
}