using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
    [JsonPropertyName("result")] public object? Result { get; set; }
    [JsonPropertyName("error")] public JsonRpcError? Error { get; set; }
}