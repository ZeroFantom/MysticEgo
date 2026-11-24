namespace Models.Autorization.Contracts.Responses;

public sealed class UserDto
{
    public Guid Id { get; set; }
    public string Login { get; set; } = "";
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public string? StatusText { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<UserRoleDto> Roles { get; set; } = new();
}