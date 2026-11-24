using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpInitializeResult
{
    [JsonPropertyName("protocolVersion")] public string ProtocolVersion { get; set; } = "2024-11-05";
    [JsonPropertyName("serverInfo")] public McpServerInfo ServerInfo { get; set; } = new();
    [JsonPropertyName("capabilities")] public McpServerCapabilities Capabilities { get; set; } = new();
}