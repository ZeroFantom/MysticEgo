namespace Models.Autorization.Configuration;

public sealed class AuthorizationOptions
{
    public string ConnectionString { get; set; } = "Data Source=Data/auth.db";
    public JwtOptions Jwt { get; set; } = new();
    public PasswordPolicy PasswordPolicy { get; set; } = new();
    public SessionPolicy Session { get; set; } = new();
    public BootstrapOptions Bootstrap { get; set; } = new();
    public int PermissionCacheMinutes { get; set; } = 5;
    public int UserPermissionCacheMinutes { get; set; } = 2;
}