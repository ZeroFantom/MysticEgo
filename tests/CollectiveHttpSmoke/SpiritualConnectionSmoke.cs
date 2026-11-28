using MysticEgo.Soul;

internal static class SpiritualConnectionSmoke
{
    public static void Run()
    {
        var player = new SpiritualPresence("p1", "world", "a", SpiritualParticipantKind.Player);
        var peer = player with { EntityId = "p2" };
        var npc = player with { EntityId = "n1", Kind = SpiritualParticipantKind.Npc };
        var farPeer = peer with { AreaId = "b" };
        var farNpc = npc with { AreaId = "b" };

        Expect(SpiritualConnectionDecision.Local, player, npc, false);
        Expect(SpiritualConnectionDecision.Local, npc, player, false);
        Expect(SpiritualConnectionDecision.Local, player, peer, false);
        Expect(SpiritualConnectionDecision.Local, player, peer, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, farNpc, false);
        Expect(SpiritualConnectionDecision.Unavailable, player, farNpc, true);
        Expect(SpiritualConnectionDecision.Unavailable, farNpc, player, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, farPeer, false);
        Expect(SpiritualConnectionDecision.Friend, player, farPeer, true);
        Expect(SpiritualConnectionDecision.Friend, farPeer, player, true);
        // Moving, or losing friendship, must be evaluated on a new snapshot.
        Expect(SpiritualConnectionDecision.Unavailable, player with { AreaId = "b" }, npc, false);
        Expect(SpiritualConnectionDecision.Unavailable, player, farPeer, false);
        Expect(SpiritualConnectionDecision.Unavailable, player, peer with { WorldId = "other" }, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, peer with { AreaId = "" }, true);
        Expect(SpiritualConnectionDecision.Unavailable, player with { WorldId = "" }, peer, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, peer with { EntityId = " " }, true);
        Expect(SpiritualConnectionDecision.Unavailable, null, peer, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, null, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, player, true);
        Expect(SpiritualConnectionDecision.Unavailable, npc, farNpc with { EntityId = "n2", AreaId = "a" }, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, peer with { Kind = SpiritualParticipantKind.Unknown }, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, peer with { Kind = (SpiritualParticipantKind)99 }, true);
        Expect(SpiritualConnectionDecision.Unavailable, player, peer with { AreaId = "A" }, false);
        Console.WriteLine("Spiritual connection domain policy passed");
    }

    private static void Expect(SpiritualConnectionDecision expected, SpiritualPresence? sender,
        SpiritualPresence? recipient, bool friends)
    {
        var actual = SpiritualConnectionPolicy.Evaluate(sender, recipient, friends);
        if (actual != expected)
            throw new InvalidOperationException($"Spiritual connection: expected {expected}, received {actual}");
    }
}
