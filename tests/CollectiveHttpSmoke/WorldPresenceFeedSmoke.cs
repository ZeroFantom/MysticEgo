using System.Text.Json;
using MysticEgo.Soul;

internal static class WorldPresenceFeedSmoke
{
    private sealed class Clock(DateTimeOffset now):TimeProvider
    {
        public DateTimeOffset Now=now;
        public override DateTimeOffset GetUtcNow()=>Now;
    }
    public static async Task Run()
    {
        var path=Path.Combine(Path.GetTempPath(),"presence-"+Guid.NewGuid().ToString("N")+".json");
        var clock=new Clock(DateTimeOffset.UtcNow);
        var reader=new WorldPresenceFeedReader(clock);
        var a=Guid.NewGuid();var b=Guid.NewGuid();var c=Guid.NewGuid();
        WorldPlacement P(string entity,string area,Guid? owner)=>new(
            new(entity,"world-private",area,owner is null?SpiritualParticipantKind.Npc:SpiritualParticipantKind.Player),owner);
        var snapshot=new SpiritualWorldSnapshot([P("a","area-private",a),P("b","remote",b),P("c","remote",c),
            P("npc","area-private",null),P("hidden","area-private",null),P("remoteNpc","remote",null)],
            [new(a,"a"),new(b,"b"),new(c,"c")],[new("a","b"),new("a","c"),new("a","npc"),new("a","remoteNpc")],
            [new("a","b")])
        {
            ContactLabels=[new("a","Hero"),new("b","Friend"),new("c","Stranger"),new("npc","Keeper"),
                new("hidden","Hidden"),new("remoteNpc","Far NPC")],
            AreaLabels=[new("world-private","area-private","Town")]
        };
        var json=new JsonSerializerOptions(JsonSerializerDefaults.Web);
        async Task Write(SpiritualWorldSnapshot s,DateTimeOffset at)=>await File.WriteAllTextAsync(path,
            JsonSerializer.Serialize(new WorldPresenceFeed(at,s),json));
        void Check(bool ok,string label){if(!ok)throw new Exception("World presence feed: "+label);}
        try
        {
            Check(!(await reader.Read(path,a)).SnapshotAvailable,"missing");
            await Write(snapshot,clock.Now);
            var view=await reader.Read(path,a);
            Check(view.SnapshotAvailable&&view.Presence?.Area=="Town"&&view.Presence.Embodiment=="Hero","owned placement");
            Check(view.Presence!.Nearby.Single().Name=="Keeper","local only");
            Check(view.Presence.Friends.Single().Name=="Friend","remote friend only");
            Check((await reader.Read(path,Guid.NewGuid())).Presence is null,"no other account");
            var publicJson=JsonSerializer.Serialize(view);
            Check(!publicJson.Contains("world-private")&&!publicJson.Contains("area-private")&&!publicJson.Contains(a.ToString()),"no IDs");
            await Write(snapshot with {Friendships=[]},clock.Now);
            Check((await reader.Read(path,a)).Presence!.Friends.Count==0,"friendship removed");
            clock.Now=clock.Now.AddSeconds(16);
            Check(!(await reader.Read(path,a)).SnapshotAvailable,"stale");
            await Write(snapshot,clock.Now.AddSeconds(5));
            Check(!(await reader.Read(path,a)).SnapshotAvailable,"future");
            await Write(snapshot with {ActiveEmbodiments=[new(a,"b")]},clock.Now);
            Check(!(await reader.Read(path,a)).SnapshotAvailable,"wrong owner");
            await File.WriteAllTextAsync(path,"{bad");
            Check(!(await reader.Read(path,a)).SnapshotAvailable,"invalid JSON");
            Console.WriteLine("Fresh world snapshot, owned placement and filtered contact projection checks passed");
        }
        finally{File.Delete(path);}
    }
}
