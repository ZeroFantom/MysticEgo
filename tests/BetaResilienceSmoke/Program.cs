using MysticEgo.Distributed;
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
