using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpAnnotations
{
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("readOnlyHint")] public bool? ReadOnlyHint { get; set; }
    [JsonPropertyName("destructiveHint")] public bool? DestructiveHint { get; set; }
    [JsonPropertyName("idempotentHint")] public bool? IdempotentHint { get; set; }
    [JsonPropertyName("openWorldHint")] public bool? OpenWorldHint { get; set; }
}