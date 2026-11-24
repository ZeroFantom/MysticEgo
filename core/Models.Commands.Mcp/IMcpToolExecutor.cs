using System.Text.Json;

namespace Models.Commands.Mcp;

/// <summary>
/// Выполняет MCP tools/call.
/// Роутит вызов через IExecutionDispatcher — тот найдёт онлайн-клиента,
/// отправит CommandExecutionRequest через SignalR, дождётся результата.
/// </summary>
public interface IMcpToolExecutor
{
    Task<McpCallToolResult> ExecuteAsync(
        string toolName,
        JsonElement? arguments,
        CancellationToken ct = default);
}