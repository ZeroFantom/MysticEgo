namespace Models.Autorization.Domain.Entities;

public class Permission
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string Category { get; set; } = "";
    public int SortOrder { get; set; }
    public bool IsSystem { get; set; }
    public string ControllerName { get; set; } = "";
    public DateTime DiscoveredAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    public ICollection<Tool> Tools { get; set; } = new List<Tool>();
}