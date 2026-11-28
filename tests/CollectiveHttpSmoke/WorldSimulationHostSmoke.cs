using MysticEgo.Soul;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

internal static class WorldSimulationHostSmoke
{
    public static async Task Run()
    {
        var dir=Directory.CreateTempSubdirectory("simulation-host-");
        try
        {
            var path=Path.Combine(dir.FullName,"world.db");
            var store=new WorldAuthorityStore(path,TimeProvider.System);
            var areas=new[]{new SimulationArea("world","town","Town"),new SimulationArea("world","forest","Forest")};
            var routes=new[]{new SimulationPassage("world","town","forest")};
            var core=new WorldSimulationCore(store,areas,routes);
            var account=Guid.NewGuid();
            void Check(bool ok,string label){if(!ok)throw new Exception("Simulation host: "+label);}
            var cfg=new ConfigurationBuilder().Build();
            var services=new ServiceCollection();services.AddWorldSimulationHost(cfg);
            Check(!services.Any(s=>s.ServiceType==typeof(WorldSimulationHost)),"disabled by default");
            using var host=new WorldSimulationHost(store,core,TimeProvider.System);
            Check(!(await host.Submit(0,[new SelectWorldEmbodiment(account,null)])).Applied,"not running");
            await host.StartAsync(default);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                while(!host.Running)await Task.Delay(10,timeout.Token);
                var result=await host.Submit(0,[new SpawnWorldPlayer("hero",account,"world","town","Hero"),
                    new SelectWorldEmbodiment(account,"hero")]).WaitAsync(timeout.Token);
                Check(result.Applied&&result.Revision==1,"durable batch");
                while((WorldAuthorityStore.ReadCurrent(path)?.Feed.SimulationTick??0)<1)
                    await Task.Delay(20,timeout.Token);
                var first=WorldAuthorityStore.ReadCurrent(path)!;
                Check(first.Revision==1&&first.Feed.SimulationTick>=1,"tick separate from revision");
                result=await host.Submit(1,[new MoveWorldEntity("hero","forest")]).WaitAsync(timeout.Token);
                Check(result.Applied&&result.Revision==2,"movement");
                Check(WorldAuthorityStore.ReadCurrent(path)!.Feed.SimulationTick>=first.Feed.SimulationTick,"time preserved");
            }
            finally{await host.StopAsync(default);}
            var stopped=WorldAuthorityStore.ReadCurrent(path)!;
            using var restarted=new WorldSimulationHost(store,core,TimeProvider.System);
            await restarted.StartAsync(default);
            try
            {
                while(!restarted.Running)await Task.Delay(10,timeout.Token);
                await Task.Delay(3200,timeout.Token);
                Check(restarted.ActiveRevision==0&&WorldAuthorityStore.ReadCurrent(path)!.Feed.SimulationTick==stopped.Feed.SimulationTick,
                    "restart never renews old state");
            }
            finally{await restarted.StopAsync(default);}
            Console.WriteLine("Simulation host queue, logical clock, revision separation and restart checks passed");
        }
        finally{dir.Delete(true);}
    }
}
