using MysticEgo.Soul;

internal static class WorldSimulationSmoke
{
    public static async Task Run()
    {
        var dir=Directory.CreateTempSubdirectory("world-simulation-");
        try
        {
            var path=Path.Combine(dir.FullName,"world.db");
            var store=new WorldAuthorityStore(path,TimeProvider.System);
            var topology=new[]{new SimulationArea("world","town","Town"),new SimulationArea("world","forest","Forest"),
                new SimulationArea("world","vault","Vault")};
            var passages=new[]{new SimulationPassage("world","town","forest"),new SimulationPassage("world","forest","town")};
            var core=new WorldSimulationCore(store,topology,passages);
            var reader=new WorldPresenceFeedReader(TimeProvider.System);
            var a=Guid.NewGuid();var b=Guid.NewGuid();
            var npc=new SpawnWorldNpc("npc","world","town","Keeper",new("npc","Patient","Brief",["Known fact"],true));
            void Check(bool ok,string label){if(!ok)throw new Exception("Simulation: "+label);}
            Check(core.Apply(0,[new SpawnWorldPlayer("a",a,"world","town","Hero"),new SpawnWorldPlayer("b",b,"world","forest","Friend"),
                npc,new SelectWorldEmbodiment(a,"a"),new SelectWorldEmbodiment(b,"b"),
                new SetSpiritualPermission("a","npc",true),new SetSpiritualPermission("npc","a",true),
                new SetSpiritualPermission("a","b",true),new SetConfirmedWorldFriendship("a","b",true)],out var rev)&&rev==1,"spawn");
            var view=(await reader.ReadConfigured(path,null,a)).Presence!;
            Check(view.Nearby.Single().Name=="Keeper"&&view.Friends.Single().Name=="Friend","local and distant friend");
            Check(!core.Apply(1,[new MoveWorldEntity("a","forest"),new MoveWorldEntity("a","vault")],out _),"invalid batch");
            Check(WorldAuthorityStore.ReadCurrent(path)!.Revision==1&&
                (await reader.ReadConfigured(path,null,a)).Presence!.Area=="Town","rollback");
            Check(!core.Apply(1,[new SelectWorldEmbodiment(a,"b")],out _),"wrong owner");
            Check(core.Apply(1,[new MoveWorldEntity("a","forest")],out rev)&&rev==2,"legal move");
            Check(!core.Apply(1,[new MoveWorldEntity("a","town")],out _),"stale revision");
            Check((await reader.ReadConfigured(path,null,a)).Presence!.Nearby.Single().Name=="Friend","movement reachability");
            Check(core.Apply(2,[new RetireWorldEntity("npc")],out rev)&&rev==3,"retire");
            Check(!core.Apply(3,[npc],out _),"cannot recycle");
            core=new WorldSimulationCore(new WorldAuthorityStore(path,TimeProvider.System),topology,passages);
            Check(core.Apply(3,[new SelectWorldEmbodiment(a,null)],out rev)&&rev==4,"restart deactivate");
            Check((await reader.ReadConfigured(path,null,a)).Presence is null,"inactive");
            Check(core.Apply(4,[new SelectWorldEmbodiment(a,"a"),new SetSpiritualPermission("a","b",false)],out rev)&&rev==5,"reactivate and revoke");
            Check((await reader.ReadConfigured(path,null,a)).Presence!.Nearby.Count==0,"grant revoked");
            Console.WriteLine("World simulation lifecycle, topology, atomic batch and spiritual permission checks passed");
        }
        finally{dir.Delete(true);}
    }
}
