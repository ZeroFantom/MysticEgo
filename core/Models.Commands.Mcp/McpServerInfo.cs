using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpServerInfo
{
    [JsonPropertyName("name")] public string Name { get; set; } = "MysticEgo.Mcp";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0.0";
}