namespace Models.Commands.Mcp;

/// <summary>
/// Диспетчер MCP-методов. Принимает JSON-RPC, возвращает JSON-RPC.
/// Никогда не бросает наружу — все ошибки в response.error.
/// </summary>
public interface IMcpDispatcher
{
    Task<JsonRpcResponse> DispatchAsync(JsonRpcRequest request, CancellationToken ct = default);
}