namespace Models.Autorization.Contracts.Responses;

public sealed class UserRoleDto
{
    public int RoleId { get; set; }
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public bool IsPrimary { get; set; }
    public DateTime AssignedAt { get; set; }
}