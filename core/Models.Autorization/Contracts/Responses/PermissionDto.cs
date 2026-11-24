namespace Models.Autorization.Contracts.Responses;

public sealed class PermissionDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public string ControllerName { get; set; } = "";
    public List<ToolDto> Tools { get; set; } = new();
}