using MysticEgo.Soul;

internal static class WorldAuthoritySmoke
{
    private sealed class Clock(DateTimeOffset now):TimeProvider
    {
        public DateTimeOffset Now=now;
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    public static async Task Run()
    {
        var dir=Directory.CreateTempSubdirectory("world-authority-");
        try
        {
            var path=Path.Combine(dir.FullName,"authority.db");
            var clock=new Clock(DateTimeOffset.UtcNow);
            var reader=new WorldPresenceFeedReader(clock);
            var a=Guid.NewGuid();var other=Guid.NewGuid();
            SpiritualWorldSnapshot Snapshot(Guid owner,string area="town",bool npc=true)
            {
                var placements=new List<WorldPlacement>{new(new("hero","world",area,SpiritualParticipantKind.Player),owner)};
                if(npc)placements.Add(new(new("keeper","world","town",SpiritualParticipantKind.Npc),null));
                return new(placements,[new(owner,"hero")],npc?[new("hero","keeper")]:[],[])
                {ContactLabels=npc?[new("hero","Hero"),new("keeper","Keeper")]:[new("hero","Hero")],
                 AreaLabels=[new("world",area,area=="town"?"Town":"Forest")]};
            }
            void Check(bool ok,string label){if(!ok)throw new Exception("World authority: "+label);}
            Check(!(await reader.ReadConfigured(path,null,a)).SnapshotAvailable&&!File.Exists(path),"read never creates");
            var first=new WorldAuthorityStore(path,clock);
            Check(WorldAuthorityStore.ReadCurrent(path) is null,"empty is unavailable");
            Check(first.TryPublish(0,Snapshot(a),out var revision)&&revision==1,"initial publication");
            var view=await reader.ReadConfigured(path,null,a);
            Check(view.Presence?.Area=="Town"&&view.Presence.Nearby.Count==1,"discovery");
            var second=new WorldAuthorityStore(path,clock);
            Check(second.TryPublish(1,Snapshot(a,"forest"),out revision)&&revision==2,"move");
            Check(!first.TryPublish(1,Snapshot(a),out revision)&&revision==2,"stale writer");
            Check((await reader.ReadConfigured(path,null,a)).Presence?.Nearby.Count==0,"movement effective");
            Check(!first.TryPublish(2,Snapshot(other,"forest"),out _),"owner reassignment denied");
            Check(second.TryPublish(2,Snapshot(a,"forest",false),out revision)&&revision==3,"retire");
            Check(!first.TryPublish(3,Snapshot(a),out _),"retired NPC cannot reappear");
            Check(WorldAuthorityStore.ReadCurrent(path)?.Revision==3,"restart durable revision");
            clock.Now=clock.Now.AddSeconds(16);
            Check(!(await reader.ReadConfigured(path,null,a)).SnapshotAvailable,"expiry");
            Check(second.TryPublish(3,Snapshot(a,"forest",false),out revision)&&revision==4,"new verified snapshot");
            Check((await reader.ReadConfigured(path,null,a)).Presence?.Area=="Forest","new state");
            Check((await reader.ReadConfigured(path,null,other)).Presence is null,"account isolation");
            Console.WriteLine("World authority CAS, persistence, movement and lifetime fencing passed");
        }
        finally{dir.Delete(true);}
    }
}
