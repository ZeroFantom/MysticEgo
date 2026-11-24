namespace Models.Autorization.Configuration;

public sealed class BootstrapOptions
{
    public string Login { get; set; } = "developer";
    public string Password { get; set; } = "";
    public string Nickname { get; set; } = "Developer";
    public string DeveloperRoleCode { get; set; } = "system.developer";
    public string SystemBypassCode { get; set; } = "system.bypass";
}