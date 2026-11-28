namespace MysticEgo.Soul;

public sealed record ActiveEmbodiment(Guid AccountId, string EntityId);
/// <summary>Directional permission from trusted game rules. Absence means unavailable.</summary>
public sealed record SpiritualPermission(string SenderId, string RecipientId);
/// <summary>Confirmed mutual player friendship for exact game entities, not social provider IDs.</summary>
public sealed record SpiritualFriendship(string FirstId, string SecondId);
public sealed record SpiritualWorldSnapshot(IReadOnlyList<WorldPlacement> Placements,
    IReadOnlyList<ActiveEmbodiment> ActiveEmbodiments, IReadOnlyList<SpiritualPermission> Permissions,
    IReadOnlyList<SpiritualFriendship> Friendships);

/// <summary>Caller is already authenticated; no client-supplied world/area/friendship or provider identity.</summary>
public sealed record SpiritualSendRequest(Guid RequestId, string RecipientId, string Text);
public enum SpiritualSendStatus { Unavailable, Accepted }
public sealed record SpiritualSendResult(SpiritualSendStatus Status, Guid? MessageId = null);
/// <summary>In-world event only; never contains account IDs, platform, bot IDs or transport credentials.</summary>
public sealed record SpiritualMessage(Guid Id, string SenderId, string RecipientId, string WorldId,
    string Text, DateTimeOffset AcceptedUtc)
{
    public string Medium => "spiritual_connection";
}

/// <summary>
/// Bounded, single-process reference application service. Not registered in HTTP/MCP or DI.
/// All world updates and inbox commits share one gate. Optional local SQLite journal; no real world writer,
/// identity revocation resolver, external delivery or NPC worker is supplied by this prototype.
/// A trusted caller must authenticate the account on each invocation. Only player-originated
/// messages are accepted here; NPC initiative requires its own trusted world entry point.
/// </summary>
public sealed class SpiritualCommunicationGateway(TimeProvider clock, SpiritualMessageJournal? journal = null)
{
    private const int MaxPending = 1024;
    private const int MaxRecords = 8192;
    private readonly object gate = new();
    private bool snapshotReady;
    private readonly WorldPresenceRegistry presence = new();
    private Dictionary<Guid, string> active = new();
    private HashSet<(string Sender, string Recipient)> permissions = new();
    private HashSet<(string First, string Second)> friends = new();
    private readonly Dictionary<(Guid Account, Guid Request), Entry> requests = new();
    private readonly Queue<Entry> pending = new();
    private readonly Dictionary<Guid, SpiritualMessage> delivered = new();
    private sealed record Entry(Guid Owner, Guid Request, SpiritualMessage Message);
    public Guid Revision { get { lock (gate) return presence.Version; } }

    /// <summary>
    /// Replace a coherent trusted snapshot. Active entity ownership, permissions and exact
    /// friendship are validated before publication. The whole update is atomic in this process.
    /// Client requests must never be used as the source of this snapshot.
    /// </summary>
    public bool TryPublish(Guid expected, SpiritualWorldSnapshot snapshot, out Guid revision)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var placements = snapshot.Placements.ToArray();
        var entities = placements.ToDictionary(x => x.Presence.EntityId, StringComparer.Ordinal);
        var nextActive = new Dictionary<Guid, string>();
        foreach (var item in snapshot.ActiveEmbodiments)
        {
            if (item.AccountId == Guid.Empty || !entities.TryGetValue(item.EntityId, out var placement) ||
                placement.AccountId != item.AccountId || placement.Presence.Kind != SpiritualParticipantKind.Player ||
                !nextActive.TryAdd(item.AccountId, item.EntityId))
                throw new ArgumentException("Invalid active embodiment.");
        }
        var nextPermissions = new HashSet<(string, string)>();
        foreach (var item in snapshot.Permissions)
        {
            if (item.SenderId == item.RecipientId || !entities.ContainsKey(item.SenderId) ||
                !entities.ContainsKey(item.RecipientId)) throw new ArgumentException("Invalid permission pair.");
            nextPermissions.Add((item.SenderId, item.RecipientId));
        }
        var nextFriends = new HashSet<(string, string)>();
        foreach (var item in snapshot.Friendships)
        {
            if (item.FirstId == item.SecondId || !entities.TryGetValue(item.FirstId, out var first) ||
                !entities.TryGetValue(item.SecondId, out var second) ||
                first.Presence.Kind != SpiritualParticipantKind.Player ||
                second.Presence.Kind != SpiritualParticipantKind.Player ||
                first.AccountId == second.AccountId) throw new ArgumentException("Invalid player friendship.");
            nextFriends.Add(FriendPair(item.FirstId, item.SecondId));
        }
        lock (gate)
        {
            if (!presence.TryReplace(expected, placements, out revision)) return false;
            active = nextActive; permissions = nextPermissions; friends = nextFriends;
            snapshotReady = true;
            return true;
        }
    }

    /// <summary>Uniform denial for inaccessible, unknown, blocked and invalid recipients.</summary>
    public SpiritualConnectionDecision Reachability(Guid account, string recipient)
    {
        lock (gate) return Resolve(account, recipient)?.Decision ?? SpiritualConnectionDecision.Unavailable;
    }

    /// <summary>
    /// Accept a bounded text message, deduplicated per authenticated account and request ID.
    /// Retries do not bypass current authorization or create another message.
    /// The request ID is not an access token; different content under the same ID is denied.
    /// </summary>
    public SpiritualSendResult Send(Guid account, SpiritualSendRequest request)
    {
        if (request is null || request.RequestId == Guid.Empty || string.IsNullOrWhiteSpace(request.Text) ||
            request.Text.Length > 4000 || request.Text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')))
            return new(SpiritualSendStatus.Unavailable);
        lock (gate)
        {
            var route = Resolve(account, request.RecipientId);
            if (route is null) return new(SpiritualSendStatus.Unavailable);
            if (journal is not null)
                return journal.Enqueue(account, request, new(Guid.NewGuid(), route.Value.Pair.Sender.EntityId,
                    request.RecipientId, route.Value.Pair.Sender.WorldId, request.Text, clock.GetUtcNow()), MaxPending, MaxRecords);
            var key = (account, request.RequestId);
            if (requests.TryGetValue(key, out var previous))
                return previous.Message.SenderId == route.Value.Pair.Sender.EntityId &&
                    previous.Message.RecipientId == request.RecipientId &&
                    previous.Message.WorldId == route.Value.Pair.Sender.WorldId &&
                    previous.Message.Text == request.Text
                    ? new(SpiritualSendStatus.Accepted, previous.Message.Id)
                    : new(SpiritualSendStatus.Unavailable);
            if (pending.Count >= MaxPending || requests.Count >= MaxRecords)
                return new(SpiritualSendStatus.Unavailable);
            var message = new SpiritualMessage(Guid.NewGuid(), route.Value.Pair.Sender.EntityId,
                request.RecipientId, route.Value.Pair.Sender.WorldId, request.Text, clock.GetUtcNow());
            var entry = new Entry(account, request.RequestId, message);
            requests.Add(key, entry);
            pending.Enqueue(entry);
            return new(SpiritualSendStatus.Accepted, message.Id);
        }
    }

    /// <summary>
    /// Trusted local worker commits one queued event to the reference world inbox under the
    /// same lock as movement, embodiment and permission updates. Denied messages are discarded,
    /// not retried automatically. This does not authorize a subsequent network delivery.
    /// </summary>
    public bool ProcessNext()
    {
        lock (gate)
        {
            if (journal is not null)
            {
                if (!snapshotReady) return false;
                var stored = journal.Next();
                if (stored is null) return false;
                var current = Resolve(stored.Owner, stored.Message.RecipientId);
                var allowed = current is not null && current.Value.Pair.Sender.EntityId == stored.Message.SenderId &&
                    current.Value.Pair.Sender.WorldId == stored.Message.WorldId;
                return journal.Finish(stored.Message.Id, allowed);
            }
            if (!pending.TryDequeue(out var entry)) return false;
            var route = Resolve(entry.Owner, entry.Message.RecipientId);
            if (route is not null && route.Value.Pair.Sender.EntityId == entry.Message.SenderId &&
                route.Value.Pair.Sender.WorldId == entry.Message.WorldId)
                delivered.Add(entry.Message.Id, entry.Message);
            return true;
        }
    }

    /// <summary>Trusted world-inbox observation for tests/internal integration; not a player history endpoint.</summary>
    public SpiritualMessage? Committed(Guid id)
    {
        lock (gate) return journal is null ? delivered.GetValueOrDefault(id) : journal.Committed(id);
    }

    private (WorldPresencePair Pair, SpiritualConnectionDecision Decision)? Resolve(Guid account, string recipient)
    {
        if (account == Guid.Empty || string.IsNullOrWhiteSpace(recipient) ||
            !active.TryGetValue(account, out var sender) || !permissions.Contains((sender, recipient)))
            return null;
        var pair = presence.ResolvePlayerPair(account, sender, recipient);
        if (pair is null) return null;
        if (pair.Recipient.Kind == SpiritualParticipantKind.Player &&
            (pair.RecipientAccountId is not Guid owner || !active.TryGetValue(owner, out var embodiment) ||
                embodiment != recipient)) return null;
        var decision = SpiritualConnectionPolicy.Evaluate(pair.Sender, pair.Recipient,
            friends.Contains(FriendPair(sender, recipient)));
        return decision == SpiritualConnectionDecision.Unavailable ? null : (pair, decision);
    }

    private static (string, string) FriendPair(string a, string b) =>
        string.CompareOrdinal(a, b) < 0 ? (a, b) : (b, a);
}
