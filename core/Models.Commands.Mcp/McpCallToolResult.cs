using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;

public sealed class McpCallToolResult
{
    [JsonPropertyName("content")] public List<McpContentBlock> Content { get; set; } = new();
    [JsonPropertyName("isError")] public bool IsError { get; set; }

    public static McpCallToolResult Ok(string text) => new()
    {
        Content = { new McpContentBlock { Type = "text", Text = text } },
        IsError = false,
    };

    public static McpCallToolResult Fail(string text) => new()
    {
        Content = { new McpContentBlock { Type = "text", Text = text } },
        IsError = true,
    };
}