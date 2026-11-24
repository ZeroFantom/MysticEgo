using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpInputSchema
{
    [JsonPropertyName("type")] public string Type { get; set; } = "object";
    [JsonPropertyName("properties")] public Dictionary<string, McpPropertySchema> Properties { get; set; } = new();
    [JsonPropertyName("required")] public List<string> Required { get; set; } = new();
    [JsonPropertyName("additionalProperties")] public bool AdditionalProperties { get; set; }
}