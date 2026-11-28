namespace MysticEgo.Soul;

public enum SpiritualParticipantKind { Unknown, Player, Npc }
public enum SpiritualConnectionDecision { Unavailable, Local, Friend }

/// <summary>
/// Authoritative world snapshot, not a client request or a messaging-provider identity.
/// Presence means a current world placement, not that the player is online.
/// </summary>
public sealed record SpiritualPresence(string EntityId, string WorldId, string AreaId, SpiritualParticipantKind Kind);

/// <summary>
/// Domain-only reachability for player communications. The caller must authenticate the
/// actor, resolve both entities and their current placement, and verify mutual friendship
/// for this exact pair from server storage. No HTTP endpoint or provider is authorized here.
/// </summary>
public static class SpiritualConnectionPolicy
{
    public static SpiritualConnectionDecision Evaluate(
        SpiritualPresence? sender, SpiritualPresence? recipient, bool confirmedPlayerFriendship)
    {
        if (!Valid(sender) || !Valid(recipient))
            return SpiritualConnectionDecision.Unavailable;

        // Valid has established the non-null participants.
        var from = sender!;
        var to = recipient!;
        if (Same(from.EntityId, to.EntityId) || !Same(from.WorldId, to.WorldId))
            return SpiritualConnectionDecision.Unavailable;
        if (from.Kind != SpiritualParticipantKind.Player && to.Kind != SpiritualParticipantKind.Player)
            return SpiritualConnectionDecision.Unavailable;
        if (Same(from.AreaId, to.AreaId))
            return SpiritualConnectionDecision.Local;
        return from.Kind == SpiritualParticipantKind.Player &&
               to.Kind == SpiritualParticipantKind.Player && confirmedPlayerFriendship
            ? SpiritualConnectionDecision.Friend
            : SpiritualConnectionDecision.Unavailable;
    }

    private static bool Valid(SpiritualPresence? presence) =>
        presence is not null &&
        !string.IsNullOrWhiteSpace(presence.EntityId) &&
        !string.IsNullOrWhiteSpace(presence.WorldId) &&
        !string.IsNullOrWhiteSpace(presence.AreaId) &&
        presence.Kind is SpiritualParticipantKind.Player or SpiritualParticipantKind.Npc;

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.Ordinal);
}
