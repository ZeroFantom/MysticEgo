using MysticEgo.Soul;

internal static class NpcRoutineSmoke
{
    public static void Run()
    {
        var areas=new[]{new SimulationArea("w","home","Home"),new SimulationArea("w","road","Road"),
            new SimulationArea("w","shop","Shop"),new SimulationArea("w","vault","Vault")};
        var links=new[]{new SimulationPassage("w","home","road"),new SimulationPassage("w","road","shop"),
            new SimulationPassage("w","shop","road"),new SimulationPassage("w","road","home")};
        var schedule=new NpcRoutine("npc","w",12,[new(0,"home","Rest"),new(4,"shop","Work"),new(10,"home","Rest")]);
        var player=new NpcRoutine("player","w",12,[new(0,"shop","Work")]);
        var planner=new NpcRoutinePlanner(areas,links,[schedule,player]);
        var owner=Guid.NewGuid();
        SpiritualWorldSnapshot Snapshot(string area)=>new(
            [new(new("npc","w",area,SpiritualParticipantKind.Npc),null),
             new(new("player","w","home",SpiritualParticipantKind.Player),owner)],
            [new(owner,"player")],[],[]);
        void Check(bool ok,string label){if(!ok)throw new Exception("NPC routine: "+label);}
        Check(planner.Plan(Snapshot("home"),3).Count==0,"rest and no player movement");
        Check(planner.Plan(Snapshot("home"),4).Single()==new MoveWorldEntity("npc","road"),"first legal hop");
        Check(planner.Plan(Snapshot("road"),5).Single()==new MoveWorldEntity("npc","shop"),"next legal hop");
        Check(planner.Plan(Snapshot("shop"),6).Count==0&&planner.Observe(Snapshot("shop"),6).Single().Activity=="Work","work intent");
        Check(planner.Plan(Snapshot("shop"),12).Single()==new MoveWorldEntity("npc","road"),"cycle repeat");
        Check(planner.Plan(Snapshot("vault"),5).Count==0,"unreachable cannot teleport");
        Check(planner.Plan(Snapshot("home") with {Placements=[]},5).Count==0,"absent NPC cannot respawn");
        try{_ = new NpcRoutinePlanner(areas,links,[schedule with {Stops=[new(1,"home","Rest")]}]);
            throw new Exception("invalid schedule accepted");}catch(ArgumentException){}
        var dir=Directory.CreateTempSubdirectory("npc-routine-");
        try
        {
            var store=new WorldAuthorityStore(Path.Combine(dir.FullName,"world.db"),TimeProvider.System);
            var core=new WorldSimulationCore(store,areas,links);
            Check(core.Apply(0,[new SpawnWorldNpc("npc","w","home","Keeper",new("npc","Patient","Brief",[],false))],out var rev),"spawn");
            Check(core.Apply(rev,planner.Plan(Snapshot("home"),4),out rev)&&rev==2,"actual core transition");
            Check(WorldAuthorityStore.ReadCurrent(Path.Combine(dir.FullName,"world.db"))!.Feed.Snapshot.Placements.Single().Presence.AreaId=="road","persisted movement");
        }
        finally{dir.Delete(true);}
        Console.WriteLine("NPC routine phases, directed path, identity isolation and persisted movement checks passed");
    }
}
