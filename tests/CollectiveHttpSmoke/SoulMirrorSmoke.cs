using System.Text.Json;
using Microsoft.Extensions.Configuration;
using MysticEgo.Soul;

internal static class SoulMirrorSmoke
{
    public static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"soul-mirror-"+Guid.NewGuid().ToString("N"));
        try
        {
            var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Soul:StoragePath",root}}).Build();
            var engine=new SoulEngine(cfg);var reader=new SoulMirrorReader(engine);
            var actor=Guid.NewGuid();var other=Guid.NewGuid();
            void Check(bool ok,string label){if(!ok)throw new Exception("Soul Mirror: "+label);}
            Check(await reader.Read(actor) is null,"read does not register");
            Check(!Directory.EnumerateFiles(root).Any(),"no record created");
            await engine.Register(actor.ToString(),default);
            await engine.Remember(actor.ToString(),new("personal","PRIVATE-MEMORY-FIXTURE"),default);
            var view=(await reader.Read(actor))!;
            Check(view.State=="EgoShard"&&view.Level==1&&view.Experience==0,"real profile");
            Check(view.Attributes.Any(x=>x.Id=="strength"&&x.Locked&&x.Value is null),"physical locked");
            Check(await reader.Read(other) is null,"no cross-account fallback");
            var json=JsonSerializer.Serialize(view);
            Check(!json.Contains("PRIVATE-MEMORY-FIXTURE")&&!json.Contains(actor.ToString())&&
                !json.Contains("SoulId")&&!json.Contains("Personality"),"projection excludes private fields");
            await engine.Quest(actor.ToString(),new("water","вода проверка"),default);
            Check((await reader.Read(actor))!.Experience>0,"reads updated canonical progress");
            Console.WriteLine("Soul Mirror read-only projection and isolation checks passed");
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
