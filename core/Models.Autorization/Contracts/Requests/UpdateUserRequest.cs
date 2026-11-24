namespace Models.Autorization.Contracts.Requests;

public sealed class UpdateUserRequest
{
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
    public string? StatusText { get; set; }
}