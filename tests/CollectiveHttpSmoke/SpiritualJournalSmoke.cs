using MysticEgo.Soul;

internal static class SpiritualJournalSmoke
{
    public static void Run()
    {
        var dir=Directory.CreateTempSubdirectory("spiritual-journal-");
        try
        {
            var file=Path.Combine(dir.FullName,"messages.db");
            var owner=Guid.NewGuid();
            SpiritualWorldSnapshot Snapshot(string area="town")=>new(
                [new(new("player","world","town",SpiritualParticipantKind.Player),owner),
                 new(new("npc","world",area,SpiritualParticipantKind.Npc),null)],
                [new(owner,"player")],[new("player","npc")],[]);
            SpiritualCommunicationGateway Open()=>new(TimeProvider.System,new SpiritualMessageJournal(file));
            void Check(bool ok,string label){if(!ok)throw new Exception("Spiritual journal: "+label);}
            void Publish(SpiritualCommunicationGateway g,string area="town")=>Check(g.TryPublish(g.Revision,Snapshot(area),out _),"publish");
            var g=Open();Publish(g);
            var first=new SpiritualSendRequest(Guid.NewGuid(),"npc","Привет");
            var second=new SpiritualSendRequest(Guid.NewGuid(),"npc","Второе");
            var a=g.Send(owner,first);var b=g.Send(owner,second);
            Check(a.Status==SpiritualSendStatus.Accepted&&b.Status==SpiritualSendStatus.Accepted,"accepted");
            g=Open();
            Check(!g.ProcessNext(),"restart requires fresh world state");
            Check(g.Send(owner,first).Status==SpiritualSendStatus.Unavailable,"restart denies new sends");
            Publish(g);
            Check(g.Send(owner,first)==a,"restart dedup");
            Check(g.Send(owner,first with {Text="changed"}).Status==SpiritualSendStatus.Unavailable,"conflict");
            Check(g.ProcessNext()&&g.Committed(a.MessageId!.Value)?.Text=="Привет","first committed");
            Publish(g,"forest");
            Check(g.ProcessNext()&&g.Committed(b.MessageId!.Value) is null,"second denied on movement");
            g=Open();Publish(g);
            Check(g.Committed(a.MessageId.Value)?.Medium=="spiritual_connection","committed survives restart");
            Check(g.Send(owner,second).Status==SpiritualSendStatus.Unavailable,"rejected attempt cannot requeue");
            Check(!g.ProcessNext(),"no duplicate delivery");
            Console.WriteLine("Spiritual SQLite journal restart, dedup and delivery recheck passed");
        }
        finally{dir.Delete(true);}
    }
}
