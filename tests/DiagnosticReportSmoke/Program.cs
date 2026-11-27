using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MysticEgo.Api.Orchestra;
using MysticEgo.Distributed;

void Check(bool result,string message){if(!result)throw new Exception(message);}
var time=DateTimeOffset.Parse("2026-01-01T00:00:00+00:00");
DiagnosticReport.Table[] data = [
    new("Summary",["field","value"],[["title","Диагностика"],["literal","=1+1"]]),
    new("Checks",["name","status","checkedUtc","latencyMs","available"],[["docker","available",time,19L,true],["voice","degraded",time,null,false]]),
    new("Telemetry",["cpuPercent","missing"],[[15.25,double.NaN]]),
    new("Links",["alias","online"],[])
];
using var json=JsonDocument.Parse(DiagnosticReport.Encode(data,"json"));
Check(json.RootElement.GetProperty("tables").GetArrayLength()==4,"JSON missing tables");
Check(json.RootElement.GetProperty("tables")[2].GetProperty("rows")[0][1].ValueKind==JsonValueKind.Null,"invalid numeric data must be null");
var text=Encoding.UTF8.GetString(DiagnosticReport.Encode(data,"txt"));
Check(text.Contains("Диагностика")&&text.Contains("[Checks]")&&text.Contains("15.25"),"TXT format");
using var archive=new ZipArchive(new MemoryStream(DiagnosticReport.Encode(data,"xlsx")),ZipArchiveMode.Read);
XDocument Read(string path){using var stream=archive.GetEntry(path)!.Open();return XDocument.Load(stream);}
XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
Check(Read("xl/workbook.xml").Descendants(ns+"sheet").Count()==4,"XLSX sheet count");
var summary=Read("xl/worksheets/sheet1.xml");
Check(!summary.Descendants(ns+"f").Any()&&summary.Descendants(ns+"t").Any(x=>x.Value=="=1+1"),"formula injection");
var checks=Read("xl/worksheets/sheet2.xml");
Check(checks.Descendants(ns+"c").Any(x=>(string?)x.Attribute("t")=="n"&&x.Value=="19"),"numeric cell");
Check(checks.Descendants(ns+"row").Count()==3,"row count");
var history=new OptionalDependencyState();
for(var i=0;i<800;i++)history.Set("auth",i%2==0?"available":"degraded");
Check(history.History().Length==720&&history.Snapshot().Length==1,"bounded history");
Check(MysticEgo.Api.Orchestra.Controllers.OrchestraUpdateController.Decide(true,true,true,true,true)=="busy","Busy must block mutation");
Check(MysticEgo.Api.Orchestra.Controllers.OrchestraUpdateController.Decide(false,true,true,true,false)=="update","Verified release priority");
Check(MysticEgo.Api.Orchestra.Controllers.OrchestraUpdateController.Decide(false,false,false,true,true)=="healthy","Healthy services must not repair");
Check(MysticEgo.Api.Orchestra.Controllers.OrchestraUpdateController.Decide(false,false,true,false,false)=="unavailable","Missing recovery channel");
Check(RuntimeSelfTests.Evaluate("available",time,time.AddMinutes(5),TimeSpan.FromSeconds(30))=="unknown","Stale observation cannot pass");
Check(RuntimeSelfTests.Evaluate("disabled",time,time,TimeSpan.FromSeconds(30))=="skip","Disabled optional service not failure");
Check(RuntimeSelfTests.Evaluate("degraded",time,time,TimeSpan.FromSeconds(30))=="fail","Fresh failure");
Check(ServiceLoadMonitor.KnownUnit("mysticego-lmstudio.service")&&!ServiceLoadMonitor.KnownUnit("../../etc/passwd"),"Fixed unit allowlist");
Check(ServiceLoadMonitor.ParseSystemdTime("Thu 2026-01-01 00:00:00 UTC")==time,"Systemd timestamp parsing");
Check(ServiceLoadMonitor.ParseSystemdTime("n/a")==null,"Missing accounting time remains unknown");
Console.WriteLine("PASS: JSON, TXT, XLSX, formula safety and bounded check history");
