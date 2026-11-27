using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;

sealed partial class CloudDesk {
    // One user session, bound to the explicitly selected Orchestra HTTPS origin.
    // Tokens are never returned to the renderer, written to disk or sent to a child node.
    readonly SemaphoreSlim topologyGate = new(1,1);
    static readonly HttpClient topologyHttp = new(new HttpClientHandler { AllowAutoRedirect=false, UseCookies=false }) { Timeout=TimeSpan.FromSeconds(10) };
    string topologyOrigin="", topologyAccess="", topologyRefresh="", topologyRootId="";
    DateTimeOffset topologyExpires;
    static string TopologyOrigin(string value) {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo!=""||u.Query!=""||u.Fragment!=""||u.AbsolutePath!="/") throw new Exception("Укажите HTTPS-адрес Orchestra без пути и параметров.");
        return u.GetLeftPart(UriPartial.Authority);
    }
    static async Task<JsonNode> TopologyRequest(string origin,string path,string bearer="",object? body=null) {
        using var req=new HttpRequestMessage(body==null?HttpMethod.Get:HttpMethod.Post,origin+path);
        if(bearer.Length>0) req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",bearer);
        if(body!=null) req.Content=JsonContent.Create(body);
        using var response=await topologyHttp.SendAsync(req,HttpCompletionOption.ResponseHeadersRead);
        if(!response.IsSuccessStatusCode) throw new HttpRequestException($"MysticEgo API: HTTP {(int)response.StatusCode} ({path})",null,response.StatusCode);
        using var ct=new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var input=await response.Content.ReadAsStreamAsync(ct.Token);
        using var buffer=new MemoryStream(); var chunk=new byte[8192]; int read;
        while((read=await input.ReadAsync(chunk.AsMemory(),ct.Token))>0){if(buffer.Length+read>8*1024*1024)throw new Exception("Ответ реестра слишком большой.");buffer.Write(chunk,0,read);}
        return JsonNode.Parse(buffer.ToArray())??throw new Exception("Пустой ответ MysticEgo API.");
    }
    void ClearTopologySession(){topologyOrigin=topologyAccess=topologyRefresh=topologyRootId="";topologyExpires=default;}
    void SetTopologySession(string origin,JsonNode result){
        if(result is not JsonObject o||S(o,"accessToken").Length==0||S(o,"refreshToken").Length==0||!DateTimeOffset.TryParse(S(o,"expiresAt"),out var expires))throw new Exception("Auth вернул неполную сессию.");
        topologyOrigin=origin;topologyAccess=S(o,"accessToken");topologyRefresh=S(o,"refreshToken");topologyExpires=expires;
    }
    async Task<object> TopologyLogin(JsonObject a){
        var origin=TopologyOrigin(S(a,"origin")); const string login="developer";var password=a["password"]?.GetValue<string>()??"";
        if(password.Length==0)throw new Exception("Укажите пароль developer.");
        await topologyGate.WaitAsync();try {
            ClearTopologySession();
            var health=await TopologyRequest(origin,"/health/live");
            if(health is not JsonObject h||!S(h,"kind").Equals("Orchestra",StringComparison.OrdinalIgnoreCase)||S(h,"status")!="alive")throw new Exception("По адресу не обнаружен Orchestra.");
            SetTopologySession(origin,await TopologyRequest(origin,"/api/auth/login",body:new{login,password}));
            try {topologyRootId=RealId(S(h,"node")); await VerifyDeveloper(origin,topologyRootId); ResetAutoAttempt();}catch{ClearTopologySession();throw;}
            return new {connected=true};
        }finally{topologyGate.Release();}
    }
    async Task<object> TopologyLogout(){await topologyGate.WaitAsync();try{ClearTopologySession();return new{connected=false};}finally{topologyGate.Release();}}
    string PairingIdentity()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(topologyAccess)));
    async Task<object> BrowserPairingStatus(){
        await topologyGate.WaitAsync();try{
            if(topologyOrigin.Length==0||topologyRefresh.Length==0||topologyRootId.Length==0||topologyExpires<=DateTimeOffset.UtcNow)
                return new{available=false};
            return new{available=true,origin=topologyOrigin,identity=PairingIdentity()};
        }finally{topologyGate.Release();}
    }
    async Task<object> BrowserPairingApprove(JsonObject args){
        var origin=TopologyOrigin(S(args,"origin"));var challenge=S(args,"challenge");
        if(!System.Text.RegularExpressions.Regex.IsMatch(challenge,@"\A[a-f0-9]{64}\z"))throw new Exception("Invalid browser challenge");
        await topologyGate.WaitAsync();try{
            if(topologyOrigin!=origin||topologyRefresh.Length==0||topologyRootId.Length==0||topologyExpires<=DateTimeOffset.UtcNow||PairingIdentity()!=S(args,"identity"))throw new Exception("Desktop session changed");
            return await TopologyRequest(origin,"/api/auth/desktop/approve",body:new{refreshToken=topologyRefresh,challenge});
        }finally{topologyGate.Release();}
    }
    static string Role(string value)=>value.ToLowerInvariant() switch{"orchestra"=>"Orchestra","global"=>"Global","local"=>"Local","agent"=>"Agent","audit" or "auditorchestra"=>"Audit",_=>""};
    static string RequiredParent(string role)=>role switch{"Global" or "Audit"=>"Orchestra","Local"=>"Global","Agent"=>"Local",_=>""};
    static string RealId(string value)=>Guid.TryParse(value,out var id)&&id!=Guid.Empty?id.ToString():"";
    static long? Stamp(JsonObject? o,string key)=>o!=null&&DateTimeOffset.TryParse(S(o,key),out var d)?d.ToUnixTimeMilliseconds():null;
    static double? Metric(JsonObject? o,string key)=>o?[key] is JsonValue v&&v.TryGetValue<double>(out var d)&&double.IsFinite(d)&&d>=0?d:null;
    static JsonObject RootNode(string id,string name,string origin)=>new(){["id"]=id,["role"]="Orchestra",["name"]=name,["parentId"]="",["host"]=origin,["healthUrl"]=origin+"/health/live",["source"]="Orchestra API",["enabled"]=true};
    static JsonObject Snapshot(JsonObject root,JsonArray records,long now,double healthMs,bool authenticated){
        var rootId=S(root,"id");var nodes=new JsonArray(root);var samples=new JsonArray(new JsonObject{["id"]=rootId,["status"]="reachable",["healthMs"]=healthMs,["edge"]="root",["at"]=now});
        var accepted=new Dictionary<string,string>{{rootId,"Orchestra"}};
        // Managed records are the only authority for identity and ParentId. A station report is optional.
        var rows=records.OfType<JsonObject>().Where(r=>r["node"] is JsonObject).ToArray();
        var duplicateIds=rows.Select(r=>RealId(S(r["node"]!.AsObject(),"id"))).GroupBy(x=>x).Where(g=>g.Count()>1).Select(g=>g.Key).ToHashSet();
        var pending=rows.ToList(); int omitted=0;
        foreach(var role in new[]{"Global","Audit","Local","Agent"}){
            foreach(var row in rows.Where(r=>Role(S(r["node"]!.AsObject(),"kind"))==role)){
                pending.Remove(row);var n=row["node"]!.AsObject();var id=RealId(S(n,"id"));var parent=RealId(S(n,"parentId"));
                if(id.Length==0||duplicateIds.Contains(id)||accepted.ContainsKey(id)||!accepted.TryGetValue(parent,out var parentRole)||parentRole!=RequiredParent(role)){omitted++;continue;}
                accepted.Add(id,role);
                var enabled=n["enabled"]?.GetValue<bool>()==true;var report=row["report"] as JsonObject;var seen=Stamp(report,"seen");var age=seen.HasValue?now-seen.Value:long.MaxValue;
                var fresh=age>=-5000&&age<=20000;
                var healthy=report?["healthy"] as JsonArray;
                var roleHealthy=healthy?.Any(v=>Role(v?.ToString()??"")==role)==true;
                var status=!enabled?"disabled":!seen.HasValue?"not-started":!fresh?"offline":roleHealthy&&S(report!,"error")==""?"reported-online":"degraded";
                var link=report?["link"] as JsonObject;var measured=Stamp(link,"measuredAt");var latency=Metric(link,"latencyMs");
                if(!enabled||!fresh||!measured.HasValue||now-measured.Value>90000||measured.Value-now>5000)latency=null;
                var name=S(n,"name");nodes.Add(new JsonObject{["id"]=id,["role"]=role,["name"]=name.Length>0?name:id,["parentId"]=parent,["host"]=S(n,"publicUrl"),["healthUrl"]="",["source"]="Orchestra registry",["enabled"]=enabled});
                samples.Add(new JsonObject{["id"]=id,["status"]=status,["edge"]=latency.HasValue?"measured":"no-sample",["latency"]=latency,["at"]=now,["seenAt"]=seen,["measuredAt"]=latency.HasValue?measured:null});
            }
        }
        return new JsonObject{["nodes"]=nodes,["samples"]=samples,["authenticated"]=authenticated,["omitted"]=omitted+pending.Count,["at"]=now};
    }
    async Task<object> TopologyPoll(JsonObject a){
        var origin=TopologyOrigin(S(a,"origin"));await topologyGate.WaitAsync();try{
            var watch=Stopwatch.StartNew();var h=(await TopologyRequest(origin,"/health/live")).AsObject();var elapsed=watch.Elapsed.TotalMilliseconds;
            var id=RealId(S(h,"node"));if(id==""||Role(S(h,"kind"))!="Orchestra"||S(h,"status")!="alive")throw new Exception("Health не подтвердил настоящий Orchestra с ID.");
            ProtectOrigin(origin);var root=RootNode(id,"Orchestra",origin);var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if(topologyOrigin!=origin||topologyRootId!=id)ClearTopologySession();
            if(topologyAccess=="" && a["autoLogin"]?.GetValue<bool>()==true){
                await TryAutoDeveloper(origin,id);
            }
            if(topologyAccess==""){var pending=Snapshot(root,new(),now,elapsed,false);pending["warning"]=autoWarning;pending["sshHost"]=autoSshHost;return pending;}
            try {
                if(DateTimeOffset.UtcNow>=topologyExpires.AddMinutes(-1))SetTopologySession(origin,await TopologyRequest(origin,"/api/auth/refresh",body:new{refreshToken=topologyRefresh}));
                var context=await VerifyDeveloper(origin,id);
                if(S(context,"rootName")!="")root["name"]=S(context,"rootName");
                var records=await TopologyRequest(origin,"/api/v1/managed/topology",topologyAccess) as JsonArray??throw new Exception("Реестр вернул неожиданный формат.");
                foreach(var row in records.OfType<JsonObject>())if(row["node"] is JsonObject n)ProtectOrigin(S(n,"publicUrl"));
                return Snapshot(root,records,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),elapsed,true);
            }catch(HttpRequestException e) when(e.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden){ClearTopologySession();var snap=Snapshot(root,new(),now,elapsed,false);snap["warning"]="Сессия developer истекла или отсутствуют права разработчика. Автовход будет повторён.";return snap;}catch(DeveloperAccessException){ClearTopologySession();throw;}
        }finally{topologyGate.Release();}
    }
    static object TopologySelfTest(){
        const string root="11111111-1111-1111-1111-111111111111", global="22222222-2222-2222-2222-222222222222",local="33333333-3333-3333-3333-333333333333",agent="44444444-4444-4444-4444-444444444444",orphan="55555555-5555-5555-5555-555555555555";
        var now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();var stamp=DateTimeOffset.FromUnixTimeMilliseconds(now).ToString("O");
        JsonObject Row(string id,string kind,string parent,bool report=true)=>new(){["node"]=new JsonObject{["id"]=id,["kind"]=kind,["parentId"]=parent,["enabled"]=true,["name"]=kind},["report"]=report?new JsonObject{["seen"]=stamp,["healthy"]=new JsonArray(JsonValue.Create(kind)),["link"]=new JsonObject{["latencyMs"]=7.5,["measuredAt"]=stamp}}:null};
        var input=new JsonArray(Row(agent,"agent",local),Row(global,"global",root),Row(local,"local",global,false),Row(orphan,"agent",root));var snap=Snapshot(RootNode(root,"Root","https://example.invalid"),input,now,1,true);var nodes=snap["nodes"]!.AsArray();var samples=snap["samples"]!.AsArray();
        if(nodes.Count!=4||S(nodes[1]!.AsObject(),"id")!=global||S(nodes[3]!.AsObject(),"parentId")!=local||snap["omitted"]!.GetValue<int>()!=1||samples[2]!["latency"]!=null||S(samples[2]!.AsObject(),"status")!="not-started"||Metric(samples[1]!.AsObject(),"latency")!=7.5)throw new Exception("Topology regression failed.");
        var stale=Snapshot(RootNode(root,"Root","https://example.invalid"),input,now+120000,1,true);if(stale["samples"]![1]!["latency"]!=null||S(stale["samples"]![1]!.AsObject(),"status")!="offline")throw new Exception("Stale report regression failed.");
        DeveloperSelfTest();
        return new{passed=2,developerChecks=5};
    }
}
