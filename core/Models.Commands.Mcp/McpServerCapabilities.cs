using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpServerCapabilities
{
    [JsonPropertyName("tools")] public object? Tools { get; set; } = new { };
    [JsonPropertyName("resources")] public object? Resources { get; set; }
}