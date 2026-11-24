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
