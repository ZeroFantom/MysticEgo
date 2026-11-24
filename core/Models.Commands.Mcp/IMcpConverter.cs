using Models.Commands.SignalR;

namespace Models.Commands.Mcp;

/// <summary>
/// Превращает CapabilityDto в MCP-совместимый tool descriptor.
/// Не зависит от ICommandRegistry — работает напрямую с capabilities.
/// </summary>
public interface IMcpConverter
{
    McpToolDescriptor ToTool(CapabilityDto capability);
    IEnumerable<McpToolDescriptor> ToTools(IEnumerable<CapabilityDto> capabilities);
}