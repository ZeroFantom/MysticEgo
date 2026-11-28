using MysticEgo.Soul;

internal static class SpiritualDialogueSmoke
{
    public static void Run()
    {
        var dir=Directory.CreateTempSubdirectory("spiritual-dialogue-");
        try
        {
            var path=Path.Combine(dir.FullName,"dialogue.db");
            var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
            var g=new SpiritualCommunicationGateway(TimeProvider.System,new SpiritualMessageJournal(path));
            SpiritualWorldSnapshot Snapshot(Guid target,string npcArea="town")=>new(
                [new(new("a","world","town",SpiritualParticipantKind.Player),a),
                 new(new("b","world","town",SpiritualParticipantKind.Player),target),
                 new(new("npc","world",npcArea,SpiritualParticipantKind.Npc),null)],
                [new(a,"a"),new(target,"b")],[new("a","b"),new("a","npc"),new("npc","a")],[])
                {ContactLabels=[new("npc","Keeper")]};
            void Check(bool ok,string label){if(!ok)throw new Exception("Dialogue: "+label);}
            void Publish(Guid target,string area="town")=>Check(g.TryPublish(g.Revision,Snapshot(target,area),out _),"publish");
            Publish(b);
            var player=g.Send(a,new(Guid.NewGuid(),"b","private"));
            Publish(c);
            Check(g.ProcessNext()&&g.Committed(player.MessageId!.Value) is null,"recipient reassignment denied");
            Publish(b);
            var first=g.Send(a,new(Guid.NewGuid(),"npc","hello"));
            Check(g.ProcessNext(),"commit player turn");
            var id=first.MessageId!.Value;
            Check(g.BeginNpcTurn(b,id) is null,"wrong owner");
            var turn=g.BeginNpcTurn(a,id)!;
            Check(turn.NpcName=="Keeper","minimal context");
            Publish(b,"forest");
            Check(!g.CommitNpcReply(a,id,turn.Revision,"late"),"movement denies reply");
            Check(g.History(a).Single().Text=="hello","history survives movement");
            Publish(b);
            Check(!g.CommitNpcReply(a,id,turn.Revision,"stale"),"old revision denied after returning");
            turn=g.BeginNpcTurn(a,id)!;
            Check(g.CommitNpcReply(a,id,turn.Revision,"welcome"),"reply");
            Check(!g.CommitNpcReply(a,id,turn.Revision,"again"),"one reply");
            Check(g.History(a).Count==2&&g.History(b).Count==0,"history isolation");
            g=new SpiritualCommunicationGateway(TimeProvider.System,new SpiritualMessageJournal(path));Publish(b);
            Check(g.History(a).Count==2&&g.BeginNpcTurn(a,id) is null,"restart persists reply");
            Console.WriteLine("Dialogue recipient fencing, NPC reply recheck and history isolation passed");
        }
        finally{dir.Delete(true);}
    }
}
