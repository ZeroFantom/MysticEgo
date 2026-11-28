using MysticEgo.Soul;

internal static class SpiritualNpcMemorySmoke
{
    private sealed class Clock(DateTimeOffset start):TimeProvider
    {
        public DateTimeOffset Now=start;
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    public static void Run()
    {
        var dir=Directory.CreateTempSubdirectory("npc-memory-");
        try
        {
            var path=Path.Combine(dir.FullName,"memory.db");
            var clock=new Clock(DateTimeOffset.UtcNow.ToUniversalTime());
            var a=Guid.NewGuid();var b=Guid.NewGuid();
            var gateway=new SpiritualCommunicationGateway(clock,new SpiritualMessageJournal(path));
            SpiritualWorldSnapshot Snapshot(bool recall=true)=>new(
                [new(new("a","world","town",SpiritualParticipantKind.Player),a),
                 new(new("b","world","town",SpiritualParticipantKind.Player),b),
                 new(new("n1","world","town",SpiritualParticipantKind.Npc),null),
                 new(new("n2","world","town",SpiritualParticipantKind.Npc),null)],
                [new(a,"a"),new(b,"b")],
                [new("a","n1"),new("n1","a"),new("a","n2"),new("n2","a"),new("b","n1"),new("n1","b")],[])
                {ContactLabels=[new("n1","Keeper"),new("n2","Smith")],
                 NpcProfiles=[new("n1","Patient","Brief",["The gate closes at dusk."],recall),
                    new("n2","Direct","Formal",["The forge is open."],true)]};
            void Check(bool ok,string label){if(!ok)throw new Exception("NPC memory: "+label);}
            void Publish(bool recall=true)=>Check(gateway.TryPublish(gateway.Revision,Snapshot(recall),out _),"publish");
            Guid Ask(Guid owner,string npc,string speech)
            {
                clock.Now=clock.Now.AddSeconds(1);
                var sent=gateway.Send(owner,new(Guid.NewGuid(),npc,speech));
                Check(sent.Status==SpiritualSendStatus.Accepted&&gateway.ProcessNext(),"queue");
                return sent.MessageId!.Value;
            }
            void Reply(Guid owner,Guid id,string text)
            {
                var turn=gateway.BeginNpcTurn(owner,id)!;
                Check(gateway.CommitNpcReply(owner,id,turn.Revision,text),"reply");
            }
            Publish();
            var first=Ask(a,"n1","FIRST");
            Reply(a,first,"ANSWER");
            Reply(b,Ask(b,"n1","OTHER-PLAYER"),"PRIVATE");
            Reply(a,Ask(a,"n2","OTHER-NPC"),"PRIVATE");
            var next=Ask(a,"n1","CURRENT");
            var turn=gateway.BeginNpcTurn(a,next)!;
            Check(turn.Character?.Personality=="Patient"&&turn.Character.KnownFacts.Single()=="The gate closes at dusk.","profile");
            Check(turn.Memory.Count==1&&turn.Memory[0]==new SpiritualNpcMemory("FIRST","ANSWER"),"exact pair only");
            var later=Ask(a,"n1","LATER");Reply(a,later,"LATER-ANSWER");
            Check(gateway.BeginNpcTurn(a,next)!.Memory.Count==1,"later exchange excluded");
            Publish(false);
            Check(gateway.BeginNpcTurn(a,next)!.Memory.Count==0,"recall disabled");
            Check(!gateway.CommitNpcReply(a,next,turn.Revision,"stale"),"profile revision fence");
            var bad=Snapshot() with {NpcProfiles=[new("a","Bad","Bad",[],true)]};
            try{gateway.TryPublish(gateway.Revision,bad,out _);throw new Exception("accepted player profile");}
            catch(ArgumentException){}
            gateway=new SpiritualCommunicationGateway(clock,new SpiritualMessageJournal(path));Publish();
            Check(gateway.BeginNpcTurn(a,next)!.Memory.Count==1,"restart durable recall");
            Console.WriteLine("NPC profile, exact pair memory, recall policy and revision checks passed");
        }
        finally{dir.Delete(true);}
    }
}
