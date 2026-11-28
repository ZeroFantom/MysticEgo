using MysticEgo.Soul;

internal static class WorldPresenceSmoke
{
    public static void Run()
    {
        var registry = new WorldPresenceRegistry();
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var a = new WorldPlacement(new("alice", "w", "a", SpiritualParticipantKind.Player), alice);
        var b = new WorldPlacement(new("bob", "w", "b", SpiritualParticipantKind.Player), bob);
        var n = new WorldPlacement(new("npc", "w", "a", SpiritualParticipantKind.Npc), null);
        void Check(bool ok, string label) { if (!ok) throw new Exception("World presence: " + label); }

        var initial = registry.Version;
        Check(registry.ResolvePlayerPair(alice, "alice", "npc") is null, "empty startup denies");
        Check(registry.TryReplace(initial, [a, b, n], out var v1), "initial publish");
        Check(v1 != initial && registry.IsCurrent(v1), "version advances");
        var pair = registry.ResolvePlayerPair(alice, "alice", "npc")!;
        Check(pair.Version == v1 && pair.RecipientAccountId is null, "atomic NPC pair");
        Check(SpiritualConnectionPolicy.Evaluate(pair.Sender, pair.Recipient, false) == SpiritualConnectionDecision.Local, "local NPC");
        Check(registry.ResolvePlayerPair(bob, "alice", "npc") is null, "ownership");
        Check(registry.ResolvePlayerPair(alice, "npc", "bob") is null, "no NPC impersonation");
        Check(registry.ResolvePlayerPair(alice, "alice", "alice") is null, "self");
        Check(registry.ResolvePlayerPair(Guid.Empty, "alice", "bob") is null, "invalid account");
        Check(registry.ResolvePlayerPair(alice, "alice", "missing") is null, "unknown target");
        var friend = registry.ResolvePlayerPair(alice, "alice", "bob")!;
        Check(friend.RecipientAccountId == bob, "exact friend account lookup");
        Check(SpiritualConnectionPolicy.Evaluate(friend.Sender, friend.Recipient, true) == SpiritualConnectionDecision.Friend, "remote confirmed friend");
        Check(SpiritualConnectionPolicy.Evaluate(friend.Sender, friend.Recipient, false) == SpiritualConnectionDecision.Unavailable, "remote nonfriend");

        var moved = a with { Presence = a.Presence with { AreaId = "b" } };
        Check(registry.TryReplace(v1, [moved, b, n], out var v2), "movement");
        Check(!registry.IsCurrent(pair.Version), "old pair invalidated");
        Check(!registry.TryReplace(v1, [a, b, n], out var observed) && observed == v2, "stale update rejected");
        var after = registry.ResolvePlayerPair(alice, "alice", "npc")!;
        Check(SpiritualConnectionPolicy.Evaluate(after.Sender, after.Recipient, false) == SpiritualConnectionDecision.Unavailable, "NPC out of area");
        Check(pair.Sender.AreaId == "a" && after.Sender.AreaId == "b", "immutable snapshot");

        void Invalid(WorldPlacement[] placements)
        {
            try { registry.TryReplace(v2, placements, out _); }
            catch (ArgumentException) { Check(registry.IsCurrent(v2), "invalid update is atomic"); return; }
            throw new Exception("Invalid placement accepted");
        }
        Invalid([a, a]);
        Invalid([a with { AccountId = null }]);
        Invalid([n with { AccountId = alice }]);
        Invalid([a with { Presence = a.Presence with { AreaId = " " } }]);
        Invalid([a with { Presence = a.Presence with { EntityId = "alice\n" } }]);
        Invalid([a with { Presence = a.Presence with { Kind = SpiritualParticipantKind.Unknown } }]);
        Check(registry.TryReplace(v2, [b, n], out var v3), "entity removal");
        Check(registry.ResolvePlayerPair(alice, "alice", "npc") is null, "removed embodiment denies");
        Check(!registry.TryReplace(v2, [a, b, n], out _), "stale resurrection denied");
        Check(new WorldPresenceRegistry().Version != v3, "new process revision not reused");

        var raceVersion = registry.Version;
        var outcomes = new bool[2];
        Parallel.Invoke(
            () => outcomes[0] = registry.TryReplace(raceVersion, [a, n], out _),
            () => outcomes[1] = registry.TryReplace(raceVersion, [b, n], out _));
        Check(outcomes.Count(x => x) == 1, "only one concurrent writer wins");
        Console.WriteLine("World presence snapshot and ownership policy passed");
    }
}
