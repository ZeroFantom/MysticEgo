using System.Security.Cryptography;

namespace MysticEgo.Distributed;

public sealed record AvatarHandoffRequest(string Motion, double Horizontal);
public sealed record AvatarHandoff(Guid Id, string Motion, double Horizontal, string Status, DateTimeOffset ExpiresAt);
public sealed record AvatarHandoffClaim(AvatarHandoff Request, string Proof);
public sealed record AvatarHandoffCompletion(string Proof, bool Started);

// One short-lived, owner-bound request per local node. No arbitrary native commands.
public sealed class AvatarHandoffQueue
{
    private readonly object gate = new();
    private AvatarHandoff? current;
    private string? owner, proof;
    private void Expire()
    {
        if (current is { } c && c.ExpiresAt <= DateTimeOffset.UtcNow)
        { current = null; owner = null; proof = null; }
    }
    public AvatarHandoff? Request(string principal, AvatarHandoffRequest request)
    {
        if (request.Motion is not ("walk" or "perch") || !double.IsFinite(request.Horizontal) ||
            request.Horizontal < 0 || request.Horizontal > 1) return null;
        lock (gate)
        {
            Expire();
            if (current is { Status: "pending" or "claimed" }) return null;
            owner = principal;
            proof = null;
            return current = new(Guid.NewGuid(), request.Motion, request.Horizontal, "pending", DateTimeOffset.UtcNow.AddSeconds(60));
        }
    }
    public AvatarHandoff? Status(string principal, Guid id)
    {
        lock (gate) { Expire(); return owner == principal && current?.Id == id ? current : null; }
    }
    public AvatarHandoffClaim? Claim(string principal)
    {
        lock (gate)
        {
            Expire();
            if (owner != principal || current is not { Status: "pending" } c) return null;
            current = c with { Status = "claimed" };
            proof = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            return new(current, proof);
        }
    }
    public bool Complete(string principal, Guid id, AvatarHandoffCompletion result)
    {
        lock (gate)
        {
            Expire();
            if (owner != principal || current is not { Status: "claimed" } c || c.Id != id ||
                string.IsNullOrEmpty(proof) || !string.Equals(proof, result.Proof, StringComparison.Ordinal)) return false;
            current = c with { Status = result.Started ? "started" : "declined", ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(2) };
            proof = null;
            return true;
        }
    }
}

