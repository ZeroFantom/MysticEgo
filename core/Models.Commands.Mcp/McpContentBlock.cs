using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpContentBlock
{
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Text { get; set; } = "";
    [JsonPropertyName("data"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Data { get; set; }
    [JsonPropertyName("mimeType"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? MimeType { get; set; }
}