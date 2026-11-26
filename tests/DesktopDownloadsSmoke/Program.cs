using System.Text.Json.Nodes;
using MysticEgo.Api.Orchestra;
JsonObject Asset(string name,string state="uploaded") => new(){["name"]=name,["state"]=state};
JsonObject Release(string tag,string date,bool draft,params JsonObject[] assets) => new(){["tag_name"]=tag,["published_at"]=date,["draft"]=draft,["assets"]=new JsonArray(assets)};
var data=new JsonArray(
 Release("v1","2026-01-01T00:00:00Z",false,Asset("MysticEgo-Desktop-win-x64.zip"),Asset("MysticEgo-Desktop-linux-x64.zip"),Asset("MysticEgo-CloudDesk-linux-x64.zip")),
 Release("v2","2026-02-01T00:00:00Z",false,Asset("MysticEgo-AuditDesktop-win-x64.exe"),Asset("MysticEgo-AgentDesktop-linux-x64.run"),Asset("MysticEgo-LocalDesktop-win-x64.exe","new"),Asset("MysticEgo-CloudDesk-linux-x64.run"),Asset("MysticEgo-ControlDesktop-win-x64.exe")),
 Release("v3","2026-03-01T00:00:00Z",true,Asset("MysticEgo-AuditDesktop-win-x64.exe")));
var user=GitHubPanel.SelectDesktopDownloads(data,false);
var owner=GitHubPanel.SelectDesktopDownloads(data,true);
void Assert(bool ok,string msg){if(!ok)throw new Exception(msg);}
Assert(user["audit-windows"].Tag=="v2"&&user["audit-windows"].Format=="exe","draft ignored");
Assert(user["agent-linux"].FileName=="MysticEgo-AgentDesktop-linux-x64.run","role-specific RUN");
Assert(!user.ContainsKey("local-windows"),"not uploaded ignored");
Assert(user["windows"].Format=="zip"&&user["linux"].Tag=="v1","legacy compatibility");
Assert(!user.ContainsKey("cloud-linux"),"no developer installers in public result");
Assert(owner["cloud-linux"].Format=="run"&&owner["control-windows"].Format=="exe","developer products");
Assert(owner["linux"].Format=="zip","developer fallback");
Console.WriteLine("Desktop download contracts passed");
