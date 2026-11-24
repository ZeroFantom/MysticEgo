using MysticEgo.Avatar;
using MysticEgo.Soul;
using Microsoft.Extensions.Configuration;
using System.Text;
using System.Buffers.Binary;
var root=Path.Combine(Path.GetTempPath(),"mystic-avatars-smoke-"+Guid.NewGuid().ToString("N"));
try
{
    var cfg=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
        ["Avatar:StoragePath"]=Path.Combine(root,"avatars"),["Soul:StoragePath"]=Path.Combine(root,"souls")
    }).Build();
    var avatars=new AvatarCatalog(cfg);
    var ct=CancellationToken.None;
    var obj=Encoding.UTF8.GetBytes("v 0 0 0\nv 1 0 0\nv 0 1 0\nf 1 2 3\n");
    Check(AvatarCatalog.ValidContent(obj,".obj"),"OBJ accepted");
    Check(!AvatarCatalog.ValidContent(Encoding.UTF8.GetBytes("<html>malicious</html>"),".obj"),"HTML refused");
    var a=await avatars.AddAsync("personal","user-A","model","companion.obj",new MemoryStream(obj),ct);
    Check(a.Size==obj.Length,"asset stored");
    await avatars.SetPersonalAsync("user-A",a.Id,ct);
    Check(avatars.GetPersonal("user-A")?.AssetId==a.Id,"personal selection stored");
    try{await avatars.SetPersonalAsync("user-B",a.Id,ct);throw new Exception("Foreign avatar accepted");}catch(ArgumentException){}
    var org=await avatars.AddAsync("organization","club","model","club.obj",new MemoryStream(obj),ct);
    var theme=await avatars.PublishAsync("club",new ThemePayload(org.Id,[],["idle","dance","sit"]),ct);
    Check(theme.Revision==1,"theme revision");
    Check(theme.ProceduralAnimations.Contains("dance")&&!theme.ProceduralAnimations.Contains("sit"),"procedural allowlist");
    var reloaded=new AvatarCatalog(cfg);Check(reloaded.Theme?.ModelId==org.Id,"theme survives restart");
    var soul=new SoulEngine(cfg);
    var user=await soul.Register("verified-user-A",ct);
    Check(!user.CanManifest&&user.State=="EgoShard","7-day weaving rule");
    var questionnaire=SoulEngine.Survey.Keys.ToDictionary(x=>x,x=>3);
    await soul.SurveyAnswers("verified-user-A",questionnaire,ct);
    await soul.ApplyCard("verified-user-A",new CardAnswers("Светлячок","friendly","balanced",3,"art",true,true),ct);
    var attempt=await soul.Quest("verified-user-A",new QuestAnswers("water","Надо проверить воду в колодце"),ct);
    var retry=await soul.Quest("verified-user-A",new QuestAnswers("water","другой ответ"),ct);
    Check(attempt.Passed&&retry==attempt,"quest result idempotent");
    var persistent=await soul.Get("verified-user-A",ct);
    Check(persistent!.Stats["knowledge"]==12,"stat granted only once");
    await soul.Remember("verified-user-A",new MemoryInput("personal","Работаю программистом"),ct);
    Check((await soul.Get("verified-user-A",ct))?.Memories.Count==1,"memory persisted");
    Console.WriteLine("PASS 12 C# Avatar/Soul smoke assertions");
}
finally{if(Directory.Exists(root))Directory.Delete(root,true);}
static void Check(bool ok,string message){if(!ok)throw new Exception("FAILED: "+message);}
