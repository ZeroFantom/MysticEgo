using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpContentBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string Text { get; set; } = "";
}