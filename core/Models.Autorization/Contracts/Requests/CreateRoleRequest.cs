namespace Models.Autorization.Contracts.Requests;

public sealed class CreateRoleRequest
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public bool IsHidden { get; set; }
    public int Priority { get; set; }
    public List<int>? PermissionIds { get; set; }
}