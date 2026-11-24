namespace Models.Autorization.Contracts.Requests;

public sealed class ReplaceRolePermissionsRequest
{
    public List<int> PermissionIds { get; set; } = new();
}