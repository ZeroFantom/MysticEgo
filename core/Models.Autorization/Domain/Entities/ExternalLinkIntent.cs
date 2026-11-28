namespace Models.Autorization.Domain.Entities;

/// <summary>Short-lived external linking state; the raw bearer challenge is never persisted.</summary>
public sealed class ExternalLinkIntent
{
    public string CodeHash { get; set; } = "";
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string Provider { get; set; } = "";
    public string Realm { get; set; } = "";
    public DateTime ExpiresUtc { get; set; }
    public string? VerifiedSubject { get; set; }
}
