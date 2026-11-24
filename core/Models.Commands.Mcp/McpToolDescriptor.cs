using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

/// <summary>
/// Готовый MCP-tool. Сериализуется 1:1 в tools/list.
/// Расширения x-* — необязательны, MCP их игнорирует.
/// </summary>
public sealed class McpToolDescriptor
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("inputSchema")] public McpInputSchema InputSchema { get; set; } = new();
    [JsonPropertyName("annotations")] public McpAnnotations Annotations { get; set; } = new();

    [JsonPropertyName("x-category")] public string? Category { get; set; }
    [JsonPropertyName("x-tags")] public List<string>? Tags { get; set; }
}