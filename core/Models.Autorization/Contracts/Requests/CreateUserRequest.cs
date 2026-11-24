namespace Models.Autorization.Contracts.Requests;

public sealed class CreateUserRequest
{
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public string? StatusText { get; set; }
    public List<int>? RoleIds { get; set; }
}