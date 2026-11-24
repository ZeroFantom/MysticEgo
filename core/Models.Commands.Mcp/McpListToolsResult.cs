using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpListToolsResult
{
    [JsonPropertyName("tools")] public List<McpToolDescriptor> Tools { get; set; } = new();
}