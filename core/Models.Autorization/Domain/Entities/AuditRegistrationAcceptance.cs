namespace Models.Autorization.Domain.Entities;

/// <summary>Records the terms and privacy notice versions shown at Audit signup.</summary>
public sealed class AuditRegistrationAcceptance
{
    public Guid UserId { get; set; }
    public required string TermsVersion { get; set; }
    public required string PrivacyVersion { get; set; }
    public DateTime AcceptedUtc { get; set; }
}
