namespace Models.Autorization.Domain.Entities;

public sealed class AccountLicenseAcceptance
{
    public Guid UserId { get; set; }
    public required string Version { get; set; }
    public DateTime AcceptedUtc { get; set; }
}

public sealed class AccountDeviceGrant
{
    public Guid UserId { get; set; }
    public Guid DeviceId { get; set; }
    public Guid GrantId { get; set; }
    public required string Scope { get; set; }
    public required string PolicyVersion { get; set; }
    public DateTime GrantedUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
}

public sealed class AccountFeatureConsent
{
    public Guid UserId { get; set; }
    public required string Scope { get; set; }
    public required string PolicyVersion { get; set; }
    public bool Enabled { get; set; }
    public DateTime UpdatedUtc { get; set; }
}
