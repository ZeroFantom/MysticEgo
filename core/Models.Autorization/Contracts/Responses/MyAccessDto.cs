namespace Models.Autorization.Contracts.Responses;

public sealed class MyAccessDto
{
    public Guid UserId { get; set; }
    public string Login { get; set; } = "";
    public bool HasBypass { get; set; }
    public int MaxPriority { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<string> Tools { get; set; } = new();
}