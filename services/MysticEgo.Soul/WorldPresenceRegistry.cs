namespace MysticEgo.Soul;

/// <summary>Placement from trusted world state; AccountId is the owning account for a player, never an NPC.</summary>
public sealed record WorldPlacement(SpiritualPresence Presence, Guid? AccountId);

/// <summary>A pair resolved from one immutable revision. Not a delivery authorization or a client token.</summary>
public sealed record WorldPresencePair(Guid Version, SpiritualPresence Sender, SpiritualPresence Recipient,
    Guid? RecipientAccountId);

/// <summary>
/// Single-process reference registry for authoritative full world snapshots. No client write endpoint,
/// no persistence and no cross-node coordination. A production world writer must supply verified
/// placements; an empty registry deliberately denies all access after startup.
/// </summary>
public sealed class WorldPresenceRegistry
{
    private readonly object _gate = new();
    private Dictionary<string, WorldPlacement> _placements = new(StringComparer.Ordinal);
    private Guid _version = Guid.NewGuid();

    public Guid Version { get { lock (_gate) return _version; } }

    /// <summary>
    /// Replace the complete registry using optimistic concurrency. Stale writers cannot overwrite
    /// newer movement. Invalid input leaves the previous snapshot untouched.
    /// </summary>
    public bool TryReplace(Guid expectedVersion, IEnumerable<WorldPlacement> placements, out Guid currentVersion)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var next = new Dictionary<string, WorldPlacement>(StringComparer.Ordinal);
        foreach (var placement in placements)
        {
            if (!Valid(placement) || !next.TryAdd(placement.Presence.EntityId, placement))
                throw new ArgumentException("Invalid or duplicate world placement.", nameof(placements));
        }
        lock (_gate)
        {
            if (_version != expectedVersion)
            {
                currentVersion = _version;
                return false;
            }
            _placements = next;
            _version = Guid.NewGuid();
            currentVersion = _version;
            return true;
        }
    }

    /// <summary>
    /// Resolve both participants atomically. The account must come from authenticated server
    /// identity; an explicitly selected entity must actually belong to that account.
    /// Active embodiment and additional world permissions remain the caller's responsibility.
    /// </summary>
    public WorldPresencePair? ResolvePlayerPair(Guid accountId, string senderId, string recipientId)
    {
        if (accountId == Guid.Empty || string.IsNullOrWhiteSpace(senderId) ||
            string.IsNullOrWhiteSpace(recipientId) || string.Equals(senderId, recipientId, StringComparison.Ordinal))
            return null;
        lock (_gate)
        {
            if (!_placements.TryGetValue(senderId, out var sender) ||
                sender.Presence.Kind != SpiritualParticipantKind.Player || sender.AccountId != accountId ||
                !_placements.TryGetValue(recipientId, out var recipient))
                return null;
            return new(_version, sender.Presence, recipient.Presence, recipient.AccountId);
        }
    }

    /// <summary>Advisory freshness check only; a delivery transaction must close the check/write race.</summary>
    public bool IsCurrent(Guid version) { lock (_gate) return _version == version; }

    private static bool Valid(WorldPlacement? placement)
    {
        if (placement?.Presence is not { } p || !ValidId(p.EntityId) ||
            !ValidId(p.WorldId) || !ValidId(p.AreaId))
            return false;
        return p.Kind switch
        {
            SpiritualParticipantKind.Player => placement.AccountId is Guid owner && owner != Guid.Empty,
            SpiritualParticipantKind.Npc => placement.AccountId is null,
            _ => false
        };
    }

    private static bool ValidId(string? value) => !string.IsNullOrWhiteSpace(value) &&
        value.Length <= 256 && string.Equals(value, value.Trim(), StringComparison.Ordinal) &&
        !value.Any(char.IsControl);
}
