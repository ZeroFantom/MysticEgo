namespace Models.Autorization.Contracts.Requests;

public sealed class UpdateRoleRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? IsHidden { get; set; }
    public int? Priority { get; set; }
}