namespace Models.Autorization.Domain.Entities;

/// <summary>One-use OAuth callback state for an existing account-link intent.</summary>
public sealed class ExternalOAuthFlow
{
    public string StateHash { get; set; } = "";
    public string BrowserHash { get; set; } = "";
    public string LinkHash { get; set; } = "";
    public string Provider { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string RedirectUri { get; set; } = "";
    public string ProtectedSecrets { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
}
