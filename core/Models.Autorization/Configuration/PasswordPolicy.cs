namespace Models.Autorization.Configuration;

public sealed class PasswordPolicy
{
    public int MinLength { get; set; } = 8;
    public int MaxLength { get; set; } = 128;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = false;
    public bool RequireNonAlphanumeric { get; set; } = false;
}