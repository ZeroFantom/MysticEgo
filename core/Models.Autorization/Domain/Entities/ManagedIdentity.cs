namespace Models.Autorization.Domain.Entities;
/// <summary>One individual credential for an agent or infrastructure node. Never store plaintext secrets.</summary>
public sealed class ManagedIdentity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "agent"; // agent, local, global
    public string SecretHash { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? LastAuthenticatedUtc { get; set; }
}
