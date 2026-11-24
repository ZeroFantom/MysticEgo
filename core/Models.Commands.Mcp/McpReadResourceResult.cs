using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpReadResourceResult
{
    [JsonPropertyName("contents")] public List<object> Contents { get; set; } = new();
}