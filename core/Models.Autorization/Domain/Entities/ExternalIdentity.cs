namespace Models.Autorization.Domain.Entities;

/// <summary>
/// Stable verified subject within a configured provider identity namespace.
/// Never stores provider access tokens, usernames, or a chat ID as identity proof.
/// </summary>
public sealed class ExternalIdentity
{
    public string Provider { get; set; } = "";
    public string Realm { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTime LinkedUtc { get; set; }
}
