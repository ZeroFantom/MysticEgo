using System.Net;
using System.Text.Json.Nodes;

sealed partial class CloudDesk {
    sealed class DeveloperAccessException(string message):Exception(message) { }
    string autoAttemptOrigin="", autoWarning="", autoSshHost="";
    DateTimeOffset autoRetryAt;
    void ResetAutoAttempt(){autoAttemptOrigin=autoWarning=autoSshHost="";autoRetryAt=default;}
    async Task<JsonObject> VerifyDeveloper(string origin,string id){
        var context=(await TopologyRequest(origin,"/api/v1/managed/context",topologyAccess)).AsObject();
        ValidateDeveloperContext(context,id);
        return context;
    }
    static void ValidateDeveloperContext(JsonObject context,string id){
        if(id.Length==0||RealId(S(context,"rootId"))!=id)throw new DeveloperAccessException("ID Orchestra в health и реестре не совпадают.");
        if(context["developer"] is not JsonValue flag||!flag.TryGetValue<bool>(out var developer)||!developer)
            throw new DeveloperAccessException("CloudDesk допускает только developer с правами разработчика MysticEgo.");
    }
    static void DeveloperSelfTest(){
        const string id="11111111-1111-1111-1111-111111111111";
        ValidateDeveloperContext(new JsonObject{["rootId"]=id,["developer"]=true},id);
        foreach(var context in new[]{new JsonObject{["rootId"]=id,["developer"]=false},new JsonObject{["rootId"]=id},new JsonObject{["rootId"]=id,["developer"]="true"},new JsonObject{["rootId"]="22222222-2222-2222-2222-222222222222",["developer"]=true}}){
            try{ValidateDeveloperContext(context,id);throw new Exception("Developer access regression failed.");}catch(DeveloperAccessException){}
        }
    }
    async Task RequireDeveloper(string value){
        var origin=TopologyOrigin(value);await topologyGate.WaitAsync();try{
            if(topologyOrigin!=origin||topologyAccess.Length==0||topologyRootId.Length==0)throw new DeveloperAccessException("Сначала подключите существующий Orchestra под developer.");
            if(DateTimeOffset.UtcNow>=topologyExpires.AddMinutes(-1))SetTopologySession(origin,await TopologyRequest(origin,"/api/auth/refresh",body:new{refreshToken=topologyRefresh}));
            await VerifyDeveloper(origin,topologyRootId);
        }catch{ClearTopologySession();throw;}finally{topologyGate.Release();}
    }
    // The credential never leaves the VPS. The fixed script reads the installer's protected
    // credential, validates the root identity against the HTTPS health response, and talks
    // only to the local Auth service. No shell argument contains a password/token.
    const string DeveloperLoginScript = """
import json, os, sys, stat, urllib.request, uuid
class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        raise RuntimeError('redirect denied')
client = urllib.request.build_opener(urllib.request.ProxyHandler({}), NoRedirect())
try:
    with client.open('http://127.0.0.1:5500/health/live', timeout=6) as response:
        h = json.load(response)
    if h.get('kind','').lower() != 'orchestra' or h.get('status') != 'alive' or uuid.UUID(h['node']) != uuid.UUID(sys.argv[1]):
        raise RuntimeError('root identity mismatch')
    path = '/var/lib/mysticego/owner-password.txt'
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'r', encoding='utf-8') as f:
        info = os.fstat(f.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_mode & 0o077 or info.st_size > 4096:
            raise RuntimeError('unsafe credential file')
        password = f.read().strip()
    if not password:
        raise RuntimeError('missing credential')
    req = urllib.request.Request('http://127.0.0.1:5400/api/auth/login', data=json.dumps({'login':'developer','password':password}).encode(), headers={'Content-Type':'application/json'}, method='POST')
    with client.open(req, timeout=12) as response:
        session = json.load(response)
    print(json.dumps({k:session[k] for k in ('accessToken','refreshToken','expiresAt')}))
except Exception:
    sys.exit(1)
""";
    static string ShellQuote(string value)=>"'"+value.Replace("'","'\"'\"'")+"'";
    async Task<object> RevealDeveloperPassword(JsonObject args){
        var origin=TopologyOrigin(S(args,"origin"));
        await RequireDeveloper(origin);
        var health=(await TopologyRequest(origin,"/health/live")).AsObject();
        var id=RealId(S(health,"node"));
        if(id.Length==0||Role(S(health,"kind"))!="Orchestra"||S(health,"status")!="alive")
            throw new DeveloperAccessException("HTTPS не подтвердил Orchestra.");
        var host=new Uri(origin).Host;
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var addresses=await Dns.GetHostAddressesAsync(host,timeout.Token);
        var ipv4=addresses.Where(ip=>ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork)
            .Select(ip=>ip.ToString()).Distinct().ToArray();
        if(ipv4.Length!=1||!PublicAddress(ipv4[0],"ipv4"))
            throw new DeveloperAccessException("Нужен один подтверждённый публичный IPv4 Orchestra.");
        var target=new JsonObject{["host"]=ipv4[0],["user"]="root",["port"]="22"};
        CheckLocalFiles();
        if(!File.Exists(Key)||!File.Exists(Known)||(await KnownLines(target)).Length==0)
            throw new DeveloperAccessException("Сначала настройте доверенный SSH к Orchestra.");
        var script=DeveloperLoginScript.Replace(
            "print(json.dumps({k:session[k] for k in ('accessToken','refreshToken','expiresAt')}))",
            "print(json.dumps({'password': password}))",StringComparison.Ordinal);
        var result=await Exec("ssh",[..SshArgs(target),"python3 -c "+ShellQuote(script)+" "+ShellQuote(id)],minutes:1,sensitive:true);
        return new {password=JsonNode.Parse(result)?["password"]?.GetValue<string>()
            ?? throw new DeveloperAccessException("Не удалось прочитать пароль developer.")};
    }
    async Task TryAutoDeveloper(string origin,string id){
        if(autoAttemptOrigin!=origin){ResetAutoAttempt();autoAttemptOrigin=origin;}
        if(DateTimeOffset.UtcNow<autoRetryAt)return;
        autoRetryAt=DateTimeOffset.UtcNow.AddSeconds(60);autoSshHost="";
        autoWarning="Автовход developer: требуется доверенный SSH к VPS Orchestra.";
        try {
            // Authenticate automatically only on an owned, running VPS whose addresses
            // cover ALL public DNS answers for this origin. Never try credentials on candidates.
            var u=new Uri(origin);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var addresses=await Dns.GetHostAddressesAsync(u.Host,timeout.Token);
            if(addresses.Length==0||addresses.Any(ip=>!PublicAddress(ip.ToString(),ip.AddressFamily==System.Net.Sockets.AddressFamily.InterNetwork?"ipv4":"ipv6")))throw new Exception("address not public");
            var servers=(await Api("servers?limit=100"))["servers"] as JsonArray??throw new Exception("no server list");
            var possible=servers.OfType<JsonObject>().Where(s=>S(s,"status")=="on").Select(s=>{
                var ips=(s["networks"] as JsonArray??new()).OfType<JsonObject>().Where(n=>S(n,"type")=="public").SelectMany(n=>(n["ips"] as JsonArray??new()).OfType<JsonObject>()).Select(i=>S(i,"ip")).ToArray();
                return new{server=s,ips};
            }).Where(s=>addresses.Any(a=>s.ips.Any(ip=>IPAddress.TryParse(ip,out var parsed)&&a.Equals(parsed)))).ToArray();
            if(possible.Length!=1){autoWarning="Автовход: DNS Orchestra не сопоставлен однозначно с включённым VPS Timeweb. Доступен резервный вход developer.";return;}
            var verified=await PublicIps(Num(possible[0].server,"id"));
            var v4=FindPublic(verified,"ipv4");
            if(v4==""||addresses.Any(a=>!verified.OfType<JsonObject>().Any(i=>IPAddress.TryParse(S(i,"ip"),out var parsed)&&a.Equals(parsed))))throw new Exception("address mismatch");
            autoSshHost=v4;
            var target=new JsonObject{["host"]=v4,["user"]="root",["port"]="22"};
            // Strict SSH verification with existing CloudDesk identity/known_hosts. No automatic trust or key replacement.
            CheckLocalFiles();
            if(!File.Exists(Key)||!File.Exists(Known)||(await KnownLines(target)).Length==0)return;
            string payload;
            try{payload=await Exec("ssh",[..SshArgs(target),"python3 -c "+ShellQuote(DeveloperLoginScript)+" "+ShellQuote(id)],minutes:1,sensitive:true);}
            catch{autoWarning="Автовход developer: SSH отклонён или не удалось получить сессию из штатного Auth. Проверьте SSH через мастер; доступен резервный вход developer.";return;}
            SetTopologySession(origin,JsonNode.Parse(payload)??throw new Exception("empty auth response"));
            topologyRootId=id;
            await VerifyDeveloper(origin,id);
            ResetAutoAttempt();
        }catch{
            ClearTopologySession();autoWarning="Автовход developer не подтверждён. Проверьте SSH, DNS и штатную установку Orchestra; доступен резервный вход developer.";
        }finally{if(topologyAccess=="")autoRetryAt=DateTimeOffset.UtcNow.AddSeconds(60);}
    }
}
