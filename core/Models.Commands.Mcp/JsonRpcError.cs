using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class JsonRpcError
{
    [JsonPropertyName("code")] public int Code { get; set; }
    [JsonPropertyName("message")] public string Message { get; set; } = "";
    [JsonPropertyName("data")] public object? Data { get; set; }
}