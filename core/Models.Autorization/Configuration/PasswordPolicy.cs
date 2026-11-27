namespace Models.Autorization.Configuration;

public sealed class PasswordPolicy
{
    public int MinLength { get; set; } = 15;
    public int MaxLength { get; set; } = 128;
    public bool RequireDigit { get; set; } = false;
    public bool RequireLowercase { get; set; } = false;
    public bool RequireUppercase { get; set; } = false;
    public bool RequireNonAlphanumeric { get; set; } = false;
}