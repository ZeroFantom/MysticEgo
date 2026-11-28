using MysticEgo.Distributed;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Models.Commands.Resilience;

var store=new OptionalDependencyState();
if(store.Snapshot().Length!=0)throw new Exception("Initial state must be empty");
store.Set("global","unavailable",reason:"connection-failed");
store.Set("auth","available",200,19);
var status=store.Snapshot();
if(status.Length!=2 || status[0].Name!="auth" || status[1].State!="unavailable")
    throw new Exception("Individual feature failure must remain isolated");
store.Set("global","available",200,5);
if(store.Snapshot().Single(x=>x.Name=="global").State!="available")
    throw new Exception("Recovered peer must become available without host restart");
Console.WriteLine("PASS: optional feature state isolation and recovery");

var gate=new FeatureCircuitBreaker(baseDelay:TimeSpan.FromMilliseconds(50),maxDelay:TimeSpan.FromMilliseconds(100));
try {await gate.ExecuteAsync<int>("avatar-generation",_=>throw new HttpRequestException("offline"));}
catch(HttpRequestException){}
if(!gate.Status("avatar-generation").Open)throw new Exception("Circuit must isolate transient failures");
try{await gate.ExecuteAsync("avatar-generation",_=>Task.FromResult(1));
    throw new Exception("Open circuit accepted a request");}
catch(FeatureTemporarilyUnavailableException){}
if(gate.Status("account-login").Open)throw new Exception("Failure must not affect other capabilities");
gate.Success("avatar-generation");
if(gate.Status("avatar-generation").Open)throw new Exception("Reset must recover capability");
Console.WriteLine("PASS: core circuit isolation");


const string socialHtml = """
<html><head><!-- mysticego-social-start -->
<link rel="canonical" href="/"/>
<meta property="og:url" content="/"/>
<meta property="og:image" content="/og/audit.png"/>
<!-- mysticego-social-end --></head><body><a href="/private">Link</a></body></html>
""";
void SocialCheck(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    Console.WriteLine("PASS: " + message);
}
var renderedSocial = PanelSocialPreviews.Render(socialHtml, "https://orchestra.example.test");
SocialCheck(renderedSocial.Contains("content=\"https://orchestra.example.test/og/audit.png\""), "absolute public image");
SocialCheck(renderedSocial.Contains("href=\"https://orchestra.example.test/\""), "absolute canonical");
SocialCheck(renderedSocial.Contains("href=\"/private\""), "HTML outside metadata unchanged");
var embeddedSocial = PanelSocialPreviews.Render(socialHtml, "https://orchestra.example.test", true);
SocialCheck(embeddedSocial.Contains("content=\"https://orchestra.example.test/audit-portal/og/audit.png\""), "embedded Audit image");
SocialCheck(embeddedSocial.Contains("href=\"https://orchestra.example.test/audit-portal/\""), "embedded Audit canonical");
SocialCheck(PanelSocialPreviews.Render(socialHtml, "https://user:password@example.test") == socialHtml, "unsafe origin ignored");
SocialCheck(PanelSocialPreviews.Render(socialHtml, "javascript:alert(1)") == socialHtml, "non-HTTP origin ignored");
SocialCheck(PanelSocialPreviews.Render("<html>old build</html>", "https://example.test") == "<html>old build</html>", "old panel untouched");


SocialCheck(renderedSocial.Contains("property=\"og:image:secure_url\" content=\"https://orchestra.example.test/og/audit.png\""), "secure image URL");
SocialCheck(!PanelSocialPreviews.Render(socialHtml,"http://localhost:5500").Contains("og:image:secure_url"), "HTTP origin has no misleading secure URL");

// Exercise real HTTP GET and HEAD without cookies, JS, or a trusted request Host.
var fixture = Path.Combine(Path.GetTempPath(), "mystic-social-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(fixture);
try
{
    var built = Environment.GetEnvironmentVariable("MYSTICEGO_SOCIAL_DIST");
    foreach (var kind in new[]{NodeKind.Orchestra,NodeKind.Global,NodeKind.Local,NodeKind.Agent,NodeKind.AuditOrchestra})
    {
        var role=kind==NodeKind.AuditOrchestra?"audit":kind.ToString().ToLowerInvariant();
        var root=Path.Combine(fixture,role);
        var auditRoot=Path.Combine(fixture,"embedded-"+role);
        byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a1gAAAABJRU5ErkJggg==");
        foreach(var (folder,panel) in new[]{(root,role),(auditRoot,"audit")})
        {
            Directory.CreateDirectory(Path.Combine(folder,"og"));
            Directory.CreateDirectory(Path.Combine(folder,"social"));
            if(!string.IsNullOrEmpty(built))
            {
                var source=Path.Combine(built,panel,"dist");
                File.Copy(Path.Combine(source,"index.html"),Path.Combine(folder,"index.html"));
                File.Copy(Path.Combine(source,"social","share.html"),Path.Combine(folder,"social","share.html"));
                File.Copy(Path.Combine(source,"og",panel+".png"),Path.Combine(folder,"og",panel+".png"));
            }
            else
            {
                var html=socialHtml.Replace("/og/audit.png","/og/"+panel+".png")
                    .Replace("<html>","<html data-role=\""+panel+"\">")
                    .Replace("<head>","<head><meta property=\"og:title\" content=\"MysticEgo "+panel+"\"/>");
                File.WriteAllText(Path.Combine(folder,"index.html"),html);
                File.WriteAllText(Path.Combine(folder,"social","share.html"),html);
                File.WriteAllBytes(Path.Combine(folder,"og",panel+".png"),png);
            }
        }
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ContentRootPath=fixture,WebRootPath=root});
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Managed:AuditWebRoot"]=auditRoot;
        builder.Services.AddSingleton(new NodeOptions{Kind=kind,PublicUrl="https://"+role+".example.test"});
        await using var app=builder.Build();
        app.UsePanelSocialPreviews();
        app.UseStaticFiles();
        app.MapGet("/api/private",()=>Results.StatusCode(401));
        app.MapFallback(()=>Results.NotFound());
        await app.StartAsync();
        using var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){BaseAddress=new Uri(app.Urls.Single())};
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TelegramBot (like TwitterBot)");
        client.DefaultRequestHeaders.Host="untrusted.example";
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Forwarded-Host","untrusted.example");
        foreach(var route in new[]{"/","/share","/assistant?token=must-not-appear","/settings"})
        {
            using var response=await client.GetAsync(route);
            var html=await response.Content.ReadAsStringAsync();
            SocialCheck(response.IsSuccessStatusCode&&response.Content.Headers.ContentType?.MediaType=="text/html"&&
                html.Contains("https://"+role+".example.test/og/"+role+".png")&&
                !html.Contains("must-not-appear")&&!html.Contains("untrusted.example"),role+" crawler GET "+route);
        }
        using(var head=await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,"/")))
            SocialCheck(head.IsSuccessStatusCode&&head.Content.Headers.ContentLength>0&&(await head.Content.ReadAsByteArrayAsync()).Length==0,role+" crawler HEAD");
        using(var cover=await client.GetAsync("/og/"+role+".png"))
            SocialCheck(cover.IsSuccessStatusCode&&cover.Content.Headers.ContentType?.MediaType=="image/png"&&
                (await cover.Content.ReadAsByteArrayAsync()).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"og",role+".png"))),role+" anonymous PNG");
        using(var missing=await client.GetAsync("/og/not-a-cover.png"))SocialCheck((int)missing.StatusCode==404,role+" missing cover");
        using(var api=await client.GetAsync("/api/private"))SocialCheck((int)api.StatusCode==401,role+" private API untouched");
        if(kind==NodeKind.Orchestra)
        {
            using var audit=await client.GetAsync("/audit-portal/settings");
            var html=await audit.Content.ReadAsStringAsync();
            SocialCheck(audit.IsSuccessStatusCode&&html.Contains("https://orchestra.example.test/audit-portal/og/audit.png"),"embedded Audit nested route");
            using var cover=await client.GetAsync("/audit-portal/og/audit.png");
            SocialCheck(cover.IsSuccessStatusCode&&cover.Content.Headers.ContentType?.MediaType=="image/png","embedded Audit public image");
        }
        await app.StopAsync();
    }
}
finally { Directory.Delete(fixture,true); }

// Interface automation: no cross-owner actions or invented completion.
var interfaceControl = new InterfaceAutomation();
var frame = new InterfaceAutomation.Frame("/",[new("/","Обзор"),new("/nodes","Узлы")]);
var uiSession = interfaceControl.Open("developer-a","Orchestra",frame);
if(interfaceControl.List("developer-b").Length!=0)throw new Exception("Cross-owner interface leak");
void RejectInterface(Action operation,int code)
{
    try { operation(); throw new Exception("Unexpected interface permission"); }
    catch(AssistantApiException error) when(error.StatusCode==code){}
}
RejectInterface(()=>interfaceControl.Navigate("developer-b",uiSession.Id,"/nodes"),404);
RejectInterface(()=>interfaceControl.Navigate("developer-a",uiSession.Id,"/settings"),400);
RejectInterface(()=>interfaceControl.Open("developer-a","Orchestra",new("https://other.example",[])),400);
var queued=interfaceControl.Navigate("developer-a",uiSession.Id,"/nodes");
if(queued.Pending is null||queued.Last is not null)throw new Exception("Navigation claimed completion before UI report");
RejectInterface(()=>interfaceControl.Navigate("developer-a",uiSession.Id,"/"),409);
var incorrect=interfaceControl.Poll("developer-a",uiSession.Id,frame,new(queued.Pending.Id,"done","/nodes"));
if(incorrect.Last?.State!="failed")throw new Exception("False navigation report accepted");
queued=interfaceControl.Navigate("developer-a",uiSession.Id,"/nodes");
var actual=interfaceControl.Poll("developer-a",uiSession.Id,new("/nodes",frame.Targets),new(queued.Pending!.Id,"done","/nodes"));
if(actual.Last?.State!="done"||actual.Pending is not null)throw new Exception("Actual navigation not acknowledged");
interfaceControl.Close("developer-a",uiSession.Id);
RejectInterface(()=>interfaceControl.Inspect("developer-a",uiSession.Id),404);
Console.WriteLine("PASS: owner isolation, safe routes, bounded queue, observed navigation, revocation");

var assistantFrame = new InterfaceAutomation.Frame("/assistant",[new("/assistant?tab=persona","Характер"),new("/infrastructure/graphs/macro","Макро контроль")]);
var assistantWindow = interfaceControl.Open("developer-a","Orchestra",assistantFrame);
RejectInterface(()=>interfaceControl.Open("developer-a","Orchestra",new("/assistant?token=secret",[])),400);
RejectInterface(()=>interfaceControl.Navigate("developer-a",assistantWindow.Id,"/assistant?tab=models"),400);
var assistantQueued=interfaceControl.Navigate("developer-a",assistantWindow.Id,"/assistant?tab=persona");
var assistantObserved=interfaceControl.Poll("developer-a",assistantWindow.Id,new("/assistant?tab=persona",assistantFrame.Targets),new(assistantQueued.Pending!.Id,"done","/assistant?tab=persona"));
if(assistantObserved.Last?.State!="done")throw new Exception("Assistant tab navigation not observed");
var graphQueued=interfaceControl.Navigate("developer-a",assistantWindow.Id,"/infrastructure/graphs/macro");
if(graphQueued.Pending is null)throw new Exception("Nested infrastructure route rejected");
interfaceControl.Close("developer-a",assistantWindow.Id);
Console.WriteLine("PASS: allowlisted assistant tabs and three-segment infrastructure navigation");

 // A factory reset starts an entirely new state directory. The live endpoint must
 // not precede creation of the real node database, even before any user visits it.
var freshDirectory=Directory.CreateTempSubdirectory("mystic-fresh-node-").FullName;
try
{
    var freshPath=Path.Combine(freshDirectory,"node.db");
    var options=new NodeOptions{Kind=NodeKind.Orchestra,DataPath=freshPath,NodeId=Guid.NewGuid().ToString()};
    var freshNode=new NodeState(options);
    if(!File.Exists(freshPath)||new FileInfo(freshPath).Length==0||!await freshNode.Data.CheckHealthAsync())
        throw new Exception("Fresh SQLite node is not initialized at startup");
    if((await freshNode.Data.ListAsync<RecordDocument>()).Any())
        throw new Exception("Fresh node must not contain fake records");
    var record=new RecordDocument{Id=Guid.NewGuid(),Collection="factory-reset-smoke",Name="persisted",Data=System.Text.Json.JsonSerializer.SerializeToElement(new{fixture=true})};
    await freshNode.Data.PutAsync(record);
    var reopened=new NodeState(options);
    if((await reopened.Data.GetAsync<RecordDocument>(record.Id))?.Name!="persisted")
        throw new Exception("Node initialization modified existing data");
    Console.WriteLine("PASS: fresh SQLite node initialized before readiness; existing records preserved");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    Directory.Delete(freshDirectory,true);
}
