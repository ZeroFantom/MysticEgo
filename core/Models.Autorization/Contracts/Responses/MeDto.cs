namespace Models.Autorization.Contracts.Responses;

public sealed class MeDto
{
    public Guid Id { get; set; }
    public string Login { get; set; } = "";
    public string? Nickname { get; set; }
    public string? AvatarUrl { get; set; }
}