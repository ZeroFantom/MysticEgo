namespace Models.Autorization.Domain.Entities;

/// <summary>Anonymous, short-lived external sign-in state. Never an account-link intent.</summary>
public sealed class ExternalLoginFlow
{
    public string StateHash { get; set; } = "";
    public string BrowserHash { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ProtectedSecrets { get; set; } = "";
    public DateTime StartedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
}
