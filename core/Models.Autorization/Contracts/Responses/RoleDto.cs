namespace Models.Autorization.Contracts.Responses;

public sealed class RoleDto
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsSystem { get; set; }
    public bool IsHidden { get; set; }
    public int Priority { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<PermissionDto> Permissions { get; set; } = new();
}