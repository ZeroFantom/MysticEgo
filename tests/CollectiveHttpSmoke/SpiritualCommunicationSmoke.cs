using MysticEgo.Soul;

internal static class SpiritualCommunicationSmoke
{
    public static void Run()
    {
        var gateway = new SpiritualCommunicationGateway(TimeProvider.System);
        var a = Guid.NewGuid(); var b = Guid.NewGuid(); var c = Guid.NewGuid();
        WorldPlacement Player(string id, Guid owner, string area) =>
            new(new(id, "world", area, SpiritualParticipantKind.Player), owner);
        WorldPlacement Npc(string area) => new(new("npc", "world", area, SpiritualParticipantKind.Npc), null);
        SpiritualWorldSnapshot Snapshot(string npcArea = "town", bool friendship = true, bool grant = true,
            string actor = "a", string playerArea = "forest") => new(
            [Player("a", a, "town"), Player("a2", a, "town"), Player("b", b, playerArea),
                Player("c", c, "town"), Npc(npcArea)],
            [new(a, actor), new(b, "b"), new(c, "c")],
            grant ? [new("a", "npc"), new("a", "b"), new("a", "c"), new("a2", "npc")] : [],
            friendship ? [new("a", "b")] : []);
        void Check(bool ok, string label) { if (!ok) throw new Exception("Spiritual gateway: " + label); }
        void Publish(SpiritualWorldSnapshot s) => Check(gateway.TryPublish(gateway.Revision, s, out _), "publish");
        SpiritualSendRequest Request(string to, string text = "Привет") => new(Guid.NewGuid(), to, text);
        Check(gateway.Send(a, Request("npc")).Status == SpiritualSendStatus.Unavailable, "empty startup");
        Publish(Snapshot());
        Check(gateway.Reachability(a, "npc") == SpiritualConnectionDecision.Local, "local NPC");
        Check(gateway.Reachability(a, "b") == SpiritualConnectionDecision.Friend, "remote friend");
        Check(gateway.Reachability(a, "c") == SpiritualConnectionDecision.Local, "local stranger");
        Check(gateway.Reachability(b, "a") == SpiritualConnectionDecision.Unavailable, "directional permission");
        Check(gateway.Send(Guid.NewGuid(), Request("npc")).Status == SpiritualSendStatus.Unavailable, "unknown account");
        Check(gateway.Send(a, Request("a2")).Status == SpiritualSendStatus.Unavailable, "inactive embodiment");
        var request = Request("npc");
        var first = gateway.Send(a, request);
        Check(first.Status == SpiritualSendStatus.Accepted && first.MessageId is not null, "queued");
        Check(gateway.Send(a, request) == first, "idempotent");
        Check(gateway.Send(a, request with { Text = "different" }).Status == SpiritualSendStatus.Unavailable, "conflicting retry");
        Check(gateway.Committed(first.MessageId!.Value) is null, "not committed before worker");
        Check(gateway.ProcessNext() && !gateway.ProcessNext(), "one queue item");
        var message = gateway.Committed(first.MessageId.Value);
        Check(message?.SenderId == "a" && message.RecipientId == "npc" &&
            message.Text == request.Text && message.Medium == "spiritual_connection", "in-world envelope");
        var moving = gateway.Send(a, Request("npc"));
        Publish(Snapshot(npcArea: "forest"));
        Check(gateway.ProcessNext() && gateway.Committed(moving.MessageId!.Value) is null, "movement before delivery");
        Check(gateway.Send(a, request).Status == SpiritualSendStatus.Unavailable, "retry cannot bypass move");
        var friend = gateway.Send(a, Request("b"));
        Publish(Snapshot(friendship: false));
        Check(gateway.ProcessNext() && gateway.Committed(friend.MessageId!.Value) is null, "friendship revoked");
        Check(gateway.Reachability(a, "b") == SpiritualConnectionDecision.Unavailable, "remote stranger");
        Publish(Snapshot(friendship: false, playerArea: "town"));
        Check(gateway.Reachability(a, "b") == SpiritualConnectionDecision.Local, "local without friendship");
        var blocked = gateway.Send(a, Request("npc"));
        Publish(Snapshot(grant: false));
        Check(gateway.ProcessNext() && gateway.Committed(blocked.MessageId!.Value) is null, "permission revoked");
        Publish(Snapshot());
        var changed = gateway.Send(a, Request("npc"));
        var stale = gateway.Revision;
        Publish(Snapshot(actor: "a2"));
        Check(!gateway.TryPublish(stale, Snapshot(), out _), "stale world writer");
        Check(gateway.ProcessNext() && gateway.Committed(changed.MessageId!.Value) is null, "embodiment changed");
        Check(gateway.Send(a, Request("npc", " ")).Status == SpiritualSendStatus.Unavailable, "empty message");
        Check(gateway.Send(a, Request("npc", new string('x',4001))).Status == SpiritualSendStatus.Unavailable, "size limit");
        var invalid = Snapshot() with { ActiveEmbodiments = [new(b, "a")] };
        try { gateway.TryPublish(gateway.Revision, invalid, out _); throw new Exception("Invalid embodiment accepted"); }
        catch (ArgumentException) { }
        Check(gateway.Reachability(a, "npc") == SpiritualConnectionDecision.Local, "invalid snapshot did not mutate");
        Console.WriteLine("Spiritual communication authorization, deduplication and delivery rechecks passed");
    }
}
