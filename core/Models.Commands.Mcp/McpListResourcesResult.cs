using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpListResourcesResult
{
    [JsonPropertyName("resources")] public List<object> Resources { get; set; } = new();
}