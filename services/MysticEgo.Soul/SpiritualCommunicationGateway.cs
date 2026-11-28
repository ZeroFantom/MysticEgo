namespace MysticEgo.Soul;

public sealed record ActiveEmbodiment(Guid AccountId, string EntityId);
/// <summary>Directional permission from trusted game rules. Absence means unavailable.</summary>
public sealed record SpiritualPermission(string SenderId, string RecipientId);
/// <summary>Confirmed mutual player friendship for exact game entities, not social provider IDs.</summary>
public sealed record SpiritualFriendship(string FirstId, string SecondId);
public sealed record SpiritualWorldSnapshot(IReadOnlyList<WorldPlacement> Placements,
    IReadOnlyList<ActiveEmbodiment> ActiveEmbodiments, IReadOnlyList<SpiritualPermission> Permissions,
    IReadOnlyList<SpiritualFriendship> Friendships)
{
    // Optional trusted in-world labels. Unnamed entities are not exposed in discovery.
    public IReadOnlyList<SpiritualContactLabel> ContactLabels { get; init; } = [];
    public IReadOnlyList<SpiritualAreaLabel> AreaLabels { get; init; } = [];
}
public sealed record SpiritualAreaLabel(string WorldId,string AreaId,string Name);
public sealed record SpiritualPublicContact(string Name,string Kind,string Reachability);
public sealed record SpiritualPresenceView(string? Embodiment,string? Area,IReadOnlyList<SpiritualPublicContact> Nearby,
    IReadOnlyList<SpiritualPublicContact> Friends);
public sealed record SpiritualContactLabel(string EntityId, string Name);
public enum SpiritualContactFilter { Nearby, Friends }
public sealed record SpiritualContact(string EntityId, string Name, SpiritualParticipantKind Kind,
    SpiritualConnectionDecision Reachability);
public sealed record SpiritualContactPage(Guid Revision, IReadOnlyList<SpiritualContact> Contacts);

/// <summary>Caller is already authenticated; no client-supplied world/area/friendship or provider identity.</summary>
public sealed record SpiritualHistoryItem(Guid MessageId,string Direction,string Text,DateTimeOffset CreatedUtc);
public sealed record SpiritualNpcTurn(Guid MessageId,string NpcName,string Text);
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
    private Dictionary<string, string> labels = new(StringComparer.Ordinal);
    private Dictionary<(string World,string Area),string> areas=new();
    private readonly Dictionary<(Guid Account, Guid Request), Entry> requests = new();
    private readonly Queue<Entry> pending = new();
    private readonly Dictionary<Guid, SpiritualMessage> delivered = new();
    private sealed record Entry(Guid Owner, Guid Request, SpiritualMessage Message, SpiritualParticipantKind RecipientKind, Guid? RecipientOwner);
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
        var nextLabels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var label in snapshot.ContactLabels)
        {
            if (!entities.ContainsKey(label.EntityId) || string.IsNullOrWhiteSpace(label.Name) ||
                label.Name.Length > 80 || label.Name != label.Name.Trim() || label.Name.Any(char.IsControl) ||
                !nextLabels.TryAdd(label.EntityId, label.Name))
                throw new ArgumentException("Invalid contact label.");
        }
        var nextAreas=new Dictionary<(string,string),string>();
        var knownAreas=placements.Select(p=>(p.Presence.WorldId,p.Presence.AreaId)).ToHashSet();
        foreach(var area in snapshot.AreaLabels)
            if(!knownAreas.Contains((area.WorldId,area.AreaId))||string.IsNullOrWhiteSpace(area.Name)||
                area.Name.Length>80||area.Name!=area.Name.Trim()||area.Name.Any(char.IsControl)||
                !nextAreas.TryAdd((area.WorldId,area.AreaId),area.Name))
                throw new ArgumentException("Invalid area label.");
        lock (gate)
        {
            if (!presence.TryReplace(expected, placements, out revision)) return false;
            active = nextActive; permissions = nextPermissions; friends = nextFriends; labels = nextLabels; areas=nextAreas;
            snapshotReady = true;
            return true;
        }
    }

    /// <summary>Uniform denial for inaccessible, unknown, blocked and invalid recipients.</summary>
    public SpiritualConnectionDecision Reachability(Guid account, string recipient)
    {
        lock (gate) return Resolve(account, recipient)?.Decision ?? SpiritualConnectionDecision.Unavailable;
    }

    /// <summary>Only currently reachable named entities. No provider/account/area metadata.
    /// This coherent revision is advisory until checked atomically by SendAtRevision.</summary>
    public SpiritualContactPage Discover(Guid account, SpiritualContactFilter filter, int limit = 20)
    {
        lock (gate)
        {
            if (!active.TryGetValue(account, out var sender) || limit is < 1 or > 32 ||
                filter is not (SpiritualContactFilter.Nearby or SpiritualContactFilter.Friends))
                return new(presence.Version, []);
            var result = new List<SpiritualContact>();
            foreach (var id in labels.Keys.OrderBy(x => x, StringComparer.Ordinal))
            {
                var route = Resolve(account, id);
                if (route is null) continue;
                if (filter == SpiritualContactFilter.Nearby && route.Value.Decision != SpiritualConnectionDecision.Local ||
                    filter == SpiritualContactFilter.Friends &&
                        !friends.Contains(FriendPair(sender,id))) continue;
                result.Add(new(id, labels[id], route.Value.Pair.Recipient.Kind, route.Value.Decision));
                if (result.Count >= limit) break;
            }
            return new(presence.Version, result);
        }
    }

    /// <summary>Same coherent revision, no raw entity/world/account IDs. Missing labels are not guessed.
    /// Caller must verify snapshot freshness and account authorization.</summary>
    public SpiritualPresenceView? ReadPresence(Guid account)
    {
        lock(gate)
        {
            if(!snapshotReady||!active.TryGetValue(account,out var entity)||
                presence.OwnedPlacement(account,entity) is not { } placement)return null;
            static SpiritualPublicContact[] Public(SpiritualContactPage page)=>page.Contacts.Select(c=>
                new SpiritualPublicContact(c.Name,c.Kind==SpiritualParticipantKind.Npc?"npc":"player",
                    c.Reachability==SpiritualConnectionDecision.Local?"local":"friend")).ToArray();
            return new(labels.GetValueOrDefault(entity),areas.GetValueOrDefault((placement.WorldId,placement.AreaId)),
                Public(Discover(account,SpiritualContactFilter.Nearby)),Public(Discover(account,SpiritualContactFilter.Friends)));
        }
    }

    /// <summary>Reject stale selections under the same lock as publication and enqueue.</summary>
    public SpiritualSendResult SendAtRevision(Guid account, Guid revision, SpiritualSendRequest request)
    {
        lock (gate)
        {
            if (revision != presence.Version) return new(SpiritualSendStatus.Unavailable);
            return Send(account, request);
        }
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
                    request.RecipientId, route.Value.Pair.Sender.WorldId, request.Text, clock.GetUtcNow()),
                    route.Value.Pair.Recipient.Kind,route.Value.Pair.RecipientAccountId, MaxPending, MaxRecords);
            var key = (account, request.RequestId);
            if (requests.TryGetValue(key, out var previous))
                return previous.Message.SenderId == route.Value.Pair.Sender.EntityId &&
                    previous.Message.RecipientId == request.RecipientId &&
                    previous.Message.WorldId == route.Value.Pair.Sender.WorldId &&
                    previous.Message.Text == request.Text && previous.RecipientKind==route.Value.Pair.Recipient.Kind &&
                    previous.RecipientOwner==route.Value.Pair.RecipientAccountId
                    ? new(SpiritualSendStatus.Accepted, previous.Message.Id)
                    : new(SpiritualSendStatus.Unavailable);
            if (pending.Count >= MaxPending || requests.Count >= MaxRecords)
                return new(SpiritualSendStatus.Unavailable);
            var message = new SpiritualMessage(Guid.NewGuid(), route.Value.Pair.Sender.EntityId,
                request.RecipientId, route.Value.Pair.Sender.WorldId, request.Text, clock.GetUtcNow());
            var entry = new Entry(account, request.RequestId, message,route.Value.Pair.Recipient.Kind,route.Value.Pair.RecipientAccountId);
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
                    current.Value.Pair.Sender.WorldId == stored.Message.WorldId &&
                    current.Value.Pair.Recipient.Kind==stored.RecipientKind && current.Value.Pair.RecipientAccountId==stored.RecipientOwner;
                return journal.Finish(stored.Message.Id, allowed);
            }
            if (!pending.TryDequeue(out var entry)) return false;
            var route = Resolve(entry.Owner, entry.Message.RecipientId);
            if (route is not null && route.Value.Pair.Sender.EntityId == entry.Message.SenderId &&
                route.Value.Pair.Sender.WorldId == entry.Message.WorldId &&
                route.Value.Pair.Recipient.Kind==entry.RecipientKind && route.Value.Pair.RecipientAccountId==entry.RecipientOwner)
                delivered.Add(entry.Message.Id, entry.Message);
            return true;
        }
    }

    /// <summary>Trusted world-inbox observation for tests/internal integration; not a player history endpoint.</summary>
    public SpiritualMessage? Committed(Guid id)
    {
        lock (gate) return journal is null ? delivered.GetValueOrDefault(id) : journal.Committed(id);
    }

    /// <summary>Internal, owner-scoped history. Movement does not erase committed dialogue.
    /// A trusted caller must verify the account and current world snapshot, not accept account IDs from clients.</summary>
    public IReadOnlyList<SpiritualHistoryItem> History(Guid account,int limit=20)
    {
        lock(gate)
        {
            if(journal is null||limit is <1 or >50||!active.TryGetValue(account,out var entity)||
                presence.OwnedPlacement(account,entity) is null)return [];
            return journal.History(account,entity,limit);
        }
    }

    private JournalEntry? NpcRoute(Guid account,Guid messageId)
    {
        if(journal is null||journal.HasReply(messageId)||journal.CommittedEntry(messageId) is not { } row||
            row.Owner!=account||row.RecipientKind!=SpiritualParticipantKind.Npc||row.RecipientOwner is not null||
            clock.GetUtcNow()-row.Message.AcceptedUtc>TimeSpan.FromMinutes(2))return null;
        var route=Resolve(account,row.Message.RecipientId);
        if(route is null||route.Value.Pair.Sender.EntityId!=row.Message.SenderId||
            route.Value.Pair.Sender.WorldId!=row.Message.WorldId||route.Value.Pair.Recipient.Kind!=SpiritualParticipantKind.Npc||
            route.Value.Decision!=SpiritualConnectionDecision.Local||
            !permissions.Contains((row.Message.RecipientId,row.Message.SenderId)))return null;
        return row;
    }

    /// <summary>Trusted worker only. Minimal NPC prompt context, no provider, account IDs or personal memories.</summary>
    public SpiritualNpcTurn? BeginNpcTurn(Guid account,Guid messageId)
    {
        lock(gate)
        {
            var row=NpcRoute(account,messageId);
            return row is not null&&labels.TryGetValue(row.Message.RecipientId,out var name)?
                new(messageId,name,row.Message.Text):null;
        }
    }

    /// <summary>Commit one response after rechecking both directions. Never holds the world lock
    /// during inference. This is a reference inbox, not a network delivery guarantee.</summary>
    public bool CommitNpcReply(Guid account,Guid messageId,string text)
    {
        if(string.IsNullOrWhiteSpace(text)||text.Length>4000||
            text.Any(c=>char.IsControl(c)&&c!='\n'&&c!='\r'&&c!='\t'))return false;
        lock(gate)return NpcRoute(account,messageId) is not null&&journal!.SaveReply(messageId,text,clock.GetUtcNow());
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
