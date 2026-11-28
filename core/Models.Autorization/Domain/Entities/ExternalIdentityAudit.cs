namespace Models.Autorization.Domain.Entities;

/// <summary>Account-scoped security history; never contains provider subject, credentials or challenge.</summary>
public sealed class ExternalIdentityAudit
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid SessionId { get; set; }
    public string Provider { get; set; } = "";
    public string Realm { get; set; } = "";
    public string Action { get; set; } = "";
    public DateTime OccurredUtc { get; set; }
    public int RevokedSessions { get; set; }
}
