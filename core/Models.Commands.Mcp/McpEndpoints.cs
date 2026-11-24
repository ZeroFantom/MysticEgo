using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Models.Commands.Mcp
{
    public static class McpEndpoints
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        public static void MapMcpEndpoints(this WebApplication app, string route = "/mcp")
        {
            // ── POST /mcp — основной JSON-RPC endpoint
            app.MapPost(route, async (
                HttpContext http,
                IMcpDispatcher dispatcher,
                CancellationToken ct) =>
            {
                JsonRpcRequest? request;

                try
                {
                    request = await JsonSerializer.DeserializeAsync<JsonRpcRequest>(
                        http.Request.Body, Json, ct);
                }
                catch (JsonException ex)
                {
                    return Results.Json(new JsonRpcResponse
                    {
                        Id = null,
                        Error = new JsonRpcError
                        {
                            Code = JsonRpcErrorCodes.ParseError,
                            Message = $"Parse error: {ex.Message}",
                        },
                    }, Json);
                }

                if (request is null)
                {
                    return Results.Json(new JsonRpcResponse
                    {
                        Id = null,
                        Error = new JsonRpcError
                        {
                            Code = JsonRpcErrorCodes.InvalidRequest,
                            Message = "Invalid JSON-RPC request.",
                        },
                    }, Json);
                }

                var response = await dispatcher.DispatchAsync(request, ct);
                return Results.Json(response, Json);
            });

            // ── GET /mcp/tools — REST-зеркало для curl
            app.MapGet(route + "/tools", async (
                IMcpDispatcher dispatcher,
                CancellationToken ct) =>
            {
                var response = await dispatcher.DispatchAsync(
                    new JsonRpcRequest
                    {
                        Method = "tools/list",
                        Id = JsonSerializer.SerializeToElement(1),
                    }, ct);

                return response.Error is not null
                    ? Results.Json(response.Error, Json)
                    : Results.Json(response.Result, Json);
            });

            // ── GET /mcp/health — liveness
            app.MapGet(route + "/health", () => Results.Json(new
            {
                status = "ok",
                service = "MysticEgo.Mcp",
                protocolVersion = "2024-11-05",
                timestamp = DateTime.UtcNow,
            }));
        }
    }
}