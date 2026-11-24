namespace Models.Autorization.Contracts.Responses;

public sealed class ToolDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string HttpMethod { get; set; } = "";
    public string PathTemplate { get; set; } = "";
    public int PermissionId { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsPublic { get; set; }
}