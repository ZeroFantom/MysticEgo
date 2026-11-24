namespace Models.Autorization.Domain.Entities;

public class Tool
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string HttpMethod { get; set; } = "";
    public string PathTemplate { get; set; } = "";
    public int PermissionId { get; set; }
    public Permission Permission { get; set; } = null!;
    public bool IsEnabled { get; set; } = true;
    public bool IsPublic { get; set; }
    public DateTime DiscoveredAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}