namespace Models.Autorization.Contracts.Responses;

public sealed class DiscoverySyncResult
{
    public int PermissionsCreated { get; set; }
    public int PermissionsUpdated { get; set; }
    public int ToolsCreated { get; set; }
    public int ToolsUpdated { get; set; }
    public int ToolsRemoved { get; set; }
    public long ElapsedMs { get; set; }
}