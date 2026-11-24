using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpPropertySchema
{
    [JsonPropertyName("type")] public string Type { get; set; } = "string";
    [JsonPropertyName("description")] public string? Description { get; set; }
    [JsonPropertyName("default")] public string? Default { get; set; }
    [JsonPropertyName("enum")] public List<string>? Enum { get; set; }
    [JsonPropertyName("format")] public string? Format { get; set; }
}