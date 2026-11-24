using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Net;

var app = new CloudDesk();
// Read-only polling can continue during a long SSH/deployment operation.
var mutations = new SemaphoreSlim(1,1);
var reads = new SemaphoreSlim(4,4);
var githubReads = new SemaphoreSlim(1,1);
var tasks = new List<Task>();
while (await Console.In.ReadLineAsync() is { } line) {
    var captured = line;
    tasks.RemoveAll(t => t.IsCompleted);
    tasks.Add(Handle(captured));
}
await Task.WhenAll(tasks);
async Task Handle(string line) {
    string id = "";
    try {
        var req = JsonNode.Parse(line)!.AsObject(); id = req["id"]!.GetValue<string>();
        var action = req["action"]!.GetValue<string>();
        var gate = action is "github.status" or "github.release" ? githubReads : action is "timeweb.status" or "servers" or "catalog" or "ssh.status" or "topology.poll" or "topology.login" or "topology.logout" or "deploy.preflight" or "domains.list" or "dns.check" or "topology.discover" or "dns.suggest" or "dns.inventory" ? reads : mutations;
        await gate.WaitAsync();
        try {
            var result = await app.Run(action, req["args"]?.AsObject() ?? new());
            Console.WriteLine(JsonSerializer.Serialize(new { id, ok = true, result }));
        } finally { gate.Release(); }
    } catch (Exception ex) { Console.WriteLine(JsonSerializer.Serialize(new { id, ok = false, error = app.Redact(ex.Message) })); }
}

sealed partial class CloudDesk {
    const string Repo = "ZeroFantom/MysticEgo", Tag = "v1.0.0-beta.7-r3.1", Asset = "MysticEgo_BETA7_R3.1_CloudDesk.zip";
    readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(60) };
    string token = "";
    readonly string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    string State => Path.Combine(home, ".local", "share", "mysticego-clouddesk");
    string Key => Path.Combine(State, "id_ed25519");
    string Known => Path.Combine(State, "known_hosts");
    string GitDisabled => Path.Combine(State, "github-disconnected");
    public string Redact(string s) { var current = Volatile.Read(ref token); return current.Length > 0 ? s.Replace(current, "[REDACTED]") : s; }
    static string S(JsonObject a, string k) => a[k]?.ToString().Trim() ?? "";
    static string Need(JsonObject a, string k, string pattern) { var s = S(a,k); if (!Regex.IsMatch(s, pattern)) throw new Exception($"Некорректное поле: {k}"); return s; }
    static int Num(JsonObject a,string k) => int.TryParse(S(a,k),out var n) && n>0 ? n : throw new Exception($"Укажите ID: {k}");
    void Init() { Directory.CreateDirectory(State); File.SetUnixFileMode(State, UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute); }
    async Task<string> Exec(string cmd, IEnumerable<string> args, string? input = null, int minutes = 15, Dictionary<string,string>? environment = null, bool sensitive = false, int? timeoutSeconds = null) {
        using var p = new Process { StartInfo = new ProcessStartInfo(cmd) { UseShellExecute=false, RedirectStandardOutput=true, RedirectStandardError=true, RedirectStandardInput=true } };
        foreach (var arg in args) p.StartInfo.ArgumentList.Add(arg);
        p.StartInfo.Environment["GH_PROMPT_DISABLED"]="1";
        p.StartInfo.Environment["LC_ALL"]="C";
        if(environment!=null) foreach(var kv in environment) p.StartInfo.Environment[kv.Key]=kv.Value;
        p.Start(); var stdout=p.StandardOutput.ReadToEndAsync(); var stderr=p.StandardError.ReadToEndAsync();
        if(input!=null) await p.StandardInput.WriteAsync(input); p.StandardInput.Close();
        using var cts=new CancellationTokenSource(timeoutSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromMinutes(minutes));
        try { await p.WaitForExitAsync(cts.Token); } catch(OperationCanceledException) { if(!p.HasExited) p.Kill(true); throw new Exception($"Тайм-аут {cmd}. Проверьте состояние операции перед повтором."); }
        var o=await stdout; var e=await stderr;
        if(p.ExitCode!=0) throw new Exception(sensitive ? $"{cmd}: код {p.ExitCode} (закрытая операция авторизации)" : $"{cmd}: код {p.ExitCode}\n{Redact(e).Trim()}\n{Redact(o).Trim()}");
        return o.Trim();
    }
    async Task<JsonNode> Api(string path, object? body=null, int version=1, HttpMethod? method=null) {
        var current = Volatile.Read(ref token); if(current.Length==0) throw new Exception("Подключите Timeweb Cloud.");
        using var r=new HttpRequestMessage(method??(body==null?HttpMethod.Get:HttpMethod.Post),$"https://api.timeweb.cloud/api/v{version}/"+path);
        r.Headers.Authorization=new AuthenticationHeaderValue("Bearer",current); if(body!=null) r.Content=JsonContent.Create(body);
        using var res=await http.SendAsync(r); var raw=await res.Content.ReadAsStringAsync();
        if(!res.IsSuccessStatusCode) {
            var detail=TimewebError(r.Method.Method,version,path,(int)res.StatusCode,raw,current);
            if(res.StatusCode==HttpStatusCode.Unauthorized) Interlocked.CompareExchange(ref token,"",current);
            throw new TimewebApiException(r.Method.Method,version,path,(int)res.StatusCode,TimewebErrorCode(raw),detail);
        }
        return string.IsNullOrWhiteSpace(raw) ? new JsonObject() : JsonNode.Parse(raw) ?? new JsonObject();
    }
    static JsonNode? Safe(JsonNode? node) {
        if(node is JsonObject o) { var n=new JsonObject(); foreach(var kv in o) if(!Regex.IsMatch(kv.Key,"password|secret|token|private_key|credential|root_pass|vnc_pass",RegexOptions.IgnoreCase)) n[kv.Key]=Safe(kv.Value); return n; }
        if(node is JsonArray a) return new JsonArray(a.Select(Safe).ToArray()); return node?.DeepClone();
    }
    string Target(JsonObject a) { var host=Need(a,"host",@"^[A-Za-z0-9][A-Za-z0-9.-]{0,252}$"); var user=Need(a,"user",@"^[a-z_][a-z0-9_-]{0,31}$"); return user+"@"+host; }
    string Port(JsonObject a) { var n=Num(a,"port"); if(n>65535) throw new Exception("Некорректный SSH-порт"); return n.ToString(); }
    static readonly UnixFileMode Private = UnixFileMode.UserRead|UnixFileMode.UserWrite;
    static string[] Parts(string line) => line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
    static string PublicPart(string line) { var p=Parts(line); return p.Length>=2 && p[0]=="ssh-ed25519" && Regex.IsMatch(p[1],@"^[A-Za-z0-9+/=]+$") ? p[0]+" "+p[1] : throw new Exception("Ожидался публичный ключ ED25519."); }
    string Lookup(JsonObject a) { Target(a); var host=S(a,"host"); return Port(a)=="22"?host:$"[{host}]:{Port(a)}"; }
    string[] CommonSsh(JsonObject a) => ["-F","/dev/null","-o","StrictHostKeyChecking=yes","-o","UserKnownHostsFile="+Known,"-o","GlobalKnownHostsFile=/dev/null","-o","HostKeyAlgorithms=ssh-ed25519","-o","UpdateHostKeys=no","-o","CheckHostIP=no","-o","ConnectTimeout=10","-o","ServerAliveInterval=15","-o","ServerAliveCountMax=2","-o","ForwardAgent=no","-o","ClearAllForwardings=yes"];
    string[] KeyOptions => ["-i",Key,"-o","IdentitiesOnly=yes","-o","IdentityAgent=none","-o","BatchMode=yes","-o","PreferredAuthentications=publickey"];
    string[] SshArgs(JsonObject a) => [..CommonSsh(a),..KeyOptions,"-p",Port(a),Target(a)];
    async Task<string> SSH(JsonObject a,string command,int minutes=15) { await EnsureKey(); return await Exec("ssh",[..SshArgs(a),command],minutes:minutes); }
    async Task Upload(JsonObject a,string local,string remote) => await Exec("scp",[..CommonSsh(a),..KeyOptions,"-P",Port(a),"--",local,Target(a)+":"+remote]);
    void CheckLocalFiles() { foreach(var f in new[]{State,Key,Key+".pub",Known}) if((File.Exists(f)||Directory.Exists(f)) && (File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0) throw new Exception("Небезопасная символическая ссылка в SSH-хранилище: "+f); }
    async Task EnsureKey() {
        CheckLocalFiles(); Init();
        if(!File.Exists(Key)) {
            if(File.Exists(Key+".pub")) File.Move(Key+".pub",Key+".pub.orphan-"+Guid.NewGuid().ToString("N"));
            await Exec("ssh-keygen",["-q","-t","ed25519","-N","","-C","MysticEgo CloudDesk","-f",Key]);
        }
        File.SetUnixFileMode(Key,Private);
        string derived;
        try { derived=PublicPart(await Exec("ssh-keygen",["-y","-P","","-f",Key],minutes:1)); }
        catch { throw new Exception("Существующий приватный ключ повреждён, не ED25519 или защищён паролем. CloudDesk не заменяет приватные ключи автоматически."); }
        if(File.Exists(Key+".pub") && PublicPart(await File.ReadAllTextAsync(Key+".pub"))!=derived) throw new Exception("Публичный и приватный SSH-ключи не совпадают. Существующие файлы сохранены.");
        if(!File.Exists(Key+".pub")) await File.WriteAllTextAsync(Key+".pub",derived+" MysticEgo CloudDesk\n");
        File.SetUnixFileMode(Key+".pub",Private);
    }
    async Task<string[]> KnownLines(JsonObject a) {
        if(!File.Exists(Known)) return [];
        try { return (await Exec("ssh-keygen",["-F",Lookup(a),"-f",Known],minutes:1)).Split('\n',StringSplitOptions.RemoveEmptyEntries).Where(l=>!l.StartsWith('#')).ToArray(); }
        catch(Exception e) when(e.Message.StartsWith("ssh-keygen: код 1\n")) { return []; }
    }
    async Task<(string raw,string fingerprint)> Scan(JsonObject a) {
        var lookup=Lookup(a);
        var raw=await Exec("ssh-keyscan",["-T","8","-p",Port(a),"-t","ed25519",S(a,"host")],minutes:1);
        var rows=raw.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(Parts).Where(x=>x.Length==3 && x[0].Equals(lookup,StringComparison.OrdinalIgnoreCase) && x[1]=="ssh-ed25519" && Regex.IsMatch(x[2],@"^[A-Za-z0-9+/=]+$")).Select(x=>lookup+" "+x[1]+" "+x[2]).Distinct().ToArray();
        if(rows.Length!=1) throw new Exception("Не получен единственный ED25519-ключ сервера. Проверьте доступность SSH.");
        var fp=Parts(await Exec("ssh-keygen",["-lf","-","-E","sha256"],rows[0]+"\n",1));
        if(fp.Length<2 || !fp[1].StartsWith("SHA256:")) throw new Exception("Не удалось вычислить fingerprint.");
        return (rows[0],fp[1]);
    }
    static JsonObject Status(string state,string message,string fingerprint="") => new(){["state"]=state,["message"]=message,["fingerprint"]=fingerprint};
    async Task<JsonObject> Probe(JsonObject a,bool prepare) {
        Target(a); Port(a); CheckLocalFiles();
        if(prepare) await EnsureKey();
        if(!File.Exists(Key)) return Status("setup","SSH-ключ ещё не создан.");
        var old=await KnownLines(a);
        if(old.Length==0) {
            if(!prepare) return Status("trust","Подлинность сервера ещё не подтверждена.");
            try { var scan=await Scan(a); return Status("trust","Подтвердите отпечаток сервера через независимую консоль.",scan.fingerprint); } catch { return Status("offline","Сервер не предоставил ED25519-ключ. Проверьте состояние VPS и порт."); }
        }
        try { await Exec("ssh",[..SshArgs(a),"true"],minutes:1); return Status("ready","SSH-авторизация по ключу подтверждена."); }
        catch(Exception e) {
            if(Regex.IsMatch(e.Message,"REMOTE HOST IDENTIFICATION HAS CHANGED|Host key verification failed|No ED25519 host key is known",RegexOptions.IgnoreCase)) {
                string fp=""; if(prepare) try { fp=(await Scan(a)).fingerprint; } catch {}
                return Status("changed","Ключ хоста отличается от доверенного. Перед заменой проверьте причину в Timeweb.",fp);
            }
            if(e.Message.Contains("Permission denied",StringComparison.OrdinalIgnoreCase)) return Status("auth","Сервер доступен, но не принимает ключ CloudDesk.");
            return Status("offline","SSH недоступен: "+Redact(e.Message));
        }
    }
    async Task<JsonObject> Trust(JsonObject a) {
        await EnsureKey(); var scanned=await Scan(a);
        if(scanned.fingerprint!=S(a,"fingerprint")) throw new Exception("Введённый отпечаток не совпадает с текущим ключом сервера.");
        var old=await KnownLines(a); var parts=Parts(scanned.raw);
        if(old.Any(l=>{var x=Parts(l);return x.Length>=3 && x[1]==parts[1] && x[2]==parts[2];})) return await Probe(a,true);
        if(old.Length>0 && S(a,"replace")!="REPLACE") throw new Exception("Для изменившегося ключа нужно отдельное подтверждение.");
        var temp=Known+".new-"+Guid.NewGuid().ToString("N");
        try {
            if(File.Exists(Known)) File.Copy(Known,temp); else await File.WriteAllTextAsync(temp,""); File.SetUnixFileMode(temp,Private);
            if(old.Length>0) { File.Copy(Known,Known+".backup-"+Guid.NewGuid().ToString("N")); await Exec("ssh-keygen",["-R",Lookup(a),"-f",temp],minutes:1); }
            await File.AppendAllTextAsync(temp,"\n"+scanned.raw+"\n"); File.SetUnixFileMode(temp,Private); File.Move(temp,Known,true);
        } finally { File.Delete(temp); File.Delete(temp+".old"); }
        return await Probe(a,true);
    }
    async Task<JsonObject> WaitForKey(JsonObject a) {
        JsonObject result=Status("auth","Ожидаем применения ключа.");
        for(int i=0;i<6;i++) { if(i>0) await Task.Delay(3000); result=await Probe(a,false); if(S(result,"state")!="auth") return result; }
        result["message"]="Ключ пока не принят сервером. Возможно, Timeweb ещё применяет его. Можно повторить проверку или воспользоваться паролем."; return result;
    }
    async Task<JsonObject> CloudKey(JsonObject a) {
        if(S(a,"confirm")!="ADD_KEY") throw new Exception("Подтвердите добавление ключа на VPS.");
        if(S(a,"user")!="root" || Port(a)!="22") throw new Exception("Автоматическое добавление через Timeweb доступно для root на стандартном порту 22.");
        var id=Num(a,"serverId"); var server=(await Api("servers/"+id))["server"] as JsonObject ?? throw new Exception("Сервер не найден в аккаунте Timeweb.");
        var host=S(a,"host");
        if(server["networks"] is not JsonArray nets || !nets.OfType<JsonObject>().Any(n=>n["ips"] is JsonArray ips && ips.OfType<JsonObject>().Any(ip=>S(ip,"ip")==host && S(ip,"type")=="ipv4"))) throw new Exception("IP не совпадает с сервером Timeweb. Ключ не добавлен.");
        var state=await Probe(a,true); if(S(state,"state")!="auth") return state;
        var key=await Run("key.register",new JsonObject()) as JsonNode;
        if(!int.TryParse(key?["id"]?.ToString()??key?["ssh_key"]?["id"]?.ToString(),out var kid)||kid<=0) throw new Exception("Timeweb не вернул ID публичного ключа.");
        try { await Api($"servers/{id}/ssh-keys",new {ssh_key_ids=new[]{kid}}); }
        catch(Exception e) { var check=await Probe(a,false); if(S(check,"state")=="ready") return check; throw new Exception("Timeweb не подтвердил добавление ключа. "+Redact(e.Message)); }
        return await WaitForKey(a);
    }
    async Task<JsonObject> PasswordKey(JsonObject a) {
        if(S(a,"confirm")!="ADD_KEY") throw new Exception("Подтвердите добавление публичного ключа.");
        var password=a["password"]?.GetValue<string>()??""; a.Remove("password"); if(password.Length==0||password.Length>4096||password.Contains('\n')||password.Contains('\r')||password.Contains('\0')) throw new Exception("Введите пароль SSH (одна строка).");
        var state=await Probe(a,true); if(S(state,"state")!="auth") return state;
        var pub=PublicPart(await File.ReadAllTextAsync(Key+".pub"));
        // No password on disk or argv. The short-lived askpass child inherits it in its environment.
        var tmp=Directory.CreateTempSubdirectory("clouddesk-askpass-").FullName;
        try {
            var helper=Path.Combine(tmp,"askpass"); await File.WriteAllTextAsync(helper,"#!/bin/sh\nprintf '%s\\n' \"$CLOUDDESK_SSH_PASSWORD\"\n"); File.SetUnixFileMode(helper,Private|UnixFileMode.UserExecute);
            var env=new Dictionary<string,string>{{"SSH_ASKPASS",helper},{"SSH_ASKPASS_REQUIRE","force"},{"DISPLAY",":0"},{"CLOUDDESK_SSH_PASSWORD",password}};
            const string install="set -eu; umask 077; mkdir -p \"$HOME/.ssh\"; chmod 700 \"$HOME/.ssh\"; touch \"$HOME/.ssh/authorized_keys\"; chmod 600 \"$HOME/.ssh/authorized_keys\"; IFS= read -r k; grep -qxF -- \"$k\" \"$HOME/.ssh/authorized_keys\" || printf '\\n%s\\n' \"$k\" >> \"$HOME/.ssh/authorized_keys\"";
            try { await Exec("ssh",[..CommonSsh(a),"-o","BatchMode=no","-o","PubkeyAuthentication=no","-o","PreferredAuthentications=password","-o","NumberOfPasswordPrompts=1","-p",Port(a),Target(a),install],pub+"\n",1,env); }
            catch { throw new Exception("Вход по паролю или установка ключа не удались. Проверьте пользователя, пароль и разрешён ли парольный SSH-вход. Настройки sshd не менялись."); }
        } finally { Directory.Delete(tmp,true); }
        return await Probe(a,false);
    }
    async Task<object> OpenTerminal(JsonObject a) {
        var state=await Probe(a,true); if(S(state,"state")!="ready") throw new Exception(S(state,"message"));
        return new {args=new[]{"-e","ssh"}.Concat(SshArgs(a)).ToArray()};
    }
    public async Task<object?> Run(string action,JsonObject a) {
        switch(action) {
            case "github.status": return await GitProfile();
            case "github.release": return new {tag=Tag, asset=Asset, sha256=await GithubReleaseDigest()};
            case "github.selftest": return GithubSelftest();
            case "github.enable": Init(); File.Delete(GitDisabled); return new { enabled=true };
            case "github.disconnect": Init(); await File.WriteAllTextAsync(GitDisabled,"1"); return new { connected=false };
            case "timeweb.connect": {
                var old=token; token=S(a,"token");
                try { await Api("servers?limit=1"); } catch { token=old; throw; }
                string warning="";
                try {
                    if(a["remember"]?.GetValue<bool>()==true) await Exec("secret-tool",["store","--label=MysticEgo CloudDesk Timeweb","application","mysticego-clouddesk","provider","timeweb"],token,1);
                    else await Exec("secret-tool",["clear","application","mysticego-clouddesk","provider","timeweb"],minutes:1);
                } catch { warning="Не удалось обновить системное хранилище. Токен действует в текущей сессии; проверьте KWallet."; }
                var profile=await TimeProfile(); profile["storageWarning"]=warning; return profile;
            }
            case "timeweb.restore": {
                try { token=await Exec("secret-tool",["lookup","application","mysticego-clouddesk","provider","timeweb"],minutes:1); } catch { token=""; }
                return await TimeProfile();
            }
            case "timeweb.status": return await TimeProfile();
            case "timeweb.disconnect": token=""; try { await Exec("secret-tool",["clear","application","mysticego-clouddesk","provider","timeweb"],minutes:1); } catch { return new {connected=false,warning="Не удалось проверить удаление токена из системного хранилища."}; } return new {connected=false};
            case "topology.discover": return await DiscoverTopology(a);
            case "dns.suggest": return await SuggestDns(a);
            case "dns.inventory": return await DnsInventory(a);
            case "dns.delete": return await DeleteDns(a);
            case "naming.selftest": return NamingSelftest();
            case "dns.selftest": return DnsSelftest();
            case "dns.delete.selftest": return await DnsDeleteSelftest();
            case "domains.list": return await Domains();
            case "dns.check": return await DnsPrepare(a,true);
            case "dns.prepare": return await DnsPrepare(a,false);
            case "deploy.reserve": return await ReserveBinding(a);
            case "dns.reconcile": return await ReconcileDns(a);
            case "binding.selftest": return BindingSelftest();
            case "servers": return Safe(await Api("servers?limit=100"));
            case "catalog": return await Catalog();
            case "catalog.selftest": return CatalogSelfTest();
            case "timeweb.selftest": return TimewebErrorSelftest();
            case "key.info": await EnsureKey(); return new { path=Key, publicKey=await File.ReadAllTextAsync(Key+".pub") };
            case "key.register": { await EnsureKey(); var pub=PublicPart(await File.ReadAllTextAsync(Key+".pub")); var keys=await Api("ssh-keys"); if(keys["ssh_keys"] is JsonArray arr) foreach(var k in arr) if(k?["body"] is JsonNode body && Parts(body.ToString()).Take(2).SequenceEqual(Parts(pub))) return Safe(k); return Safe(await Api("ssh-keys",SshKeyPayload(pub))); }
            case "order.selftest": return await OrderSelftest();
            case "server.create": { string selectedLocation=""; return await SubmitPreparedOrder(async () => {
                if(S(a,"confirm")!="CREATE") throw new Exception("Подтвердите платное создание сервера");
                var name=Need(a,"name",@"^[A-Za-z0-9][A-Za-z0-9-]{0,62}$"); var role=Need(a,"role",@"^(Orchestra|Global)$");
                var preset=Num(a,"preset"); var os=Num(a,"os"); var zone=S(a,"zone");
                var dual=S(a,"network")=="dual";
                if(S(a,"network") is not ("dual" or "ipv4")) throw new Exception("Выберите режим сети");
                var fresh=await Catalog();
                var selected=fresh["plans"]!.AsArray().OfType<JsonObject>().FirstOrDefault(x=>S(x,"id")==preset.ToString() && S(x,"zone")==zone);
                if(selected==null || (dual && !DualStackZone(zone))) throw new Exception("Тариф больше не подходит или исключён из каталога. Заказ не отправлен.");
                selectedLocation=S(selected,"location"); if(selectedLocation.Length==0) selectedLocation=zone;
                if(S(selected,"availability")!="confirmed" && S(a,"capacityConsent")!="STANDARD_ONLY") throw new Exception("Timeweb не сообщает остаток мощностей. Подтвердите обычное создание без гарантии наличия; заказ не отправлен.");
                if(S(selected,"price")!=S(a,"price") || S(selected,"hourly")!=S(a,"hourly")) throw new Exception("Цена изменилась. Дождитесь автоматической синхронизации и подтвердите заново.");
                if(!fresh["systems"]!.AsArray().OfType<JsonObject>().Any(x=>S(x,"id")==os.ToString())) throw new Exception("ОС больше не доступна. Дождитесь автоматической синхронизации каталога.");
                await GitCheck();
                var key=await Run("key.register",new JsonObject()) as JsonNode; var keyId=key?["id"]?.ToString()??key?["ssh_key"]?["id"]?.ToString();
                if(!int.TryParse(keyId,out var kid)||kid<=0) throw new Exception("Не удалось зарегистрировать SSH-ключ. Сервер не создан.");
                var body=new JsonObject { ["name"]=name,["comment"]="MysticEgo "+role,["preset_id"]=preset,["os_id"]=os,["ssh_keys_ids"]=new JsonArray(JsonValue.Create(kid)) };
                var explicitZone=S(selected,"availabilityZone"); if(explicitZone.Length>0) body["availability_zone"]=explicitZone;
                body["is_ddos_guard"]=false;
                // Ordinary create only; no preorder, backup or snapshot API is invoked. Stock is not guaranteed by the public catalog.
                // Allocate only missing addresses on this exact VPS, never create another VPS on error.
                return body;
            }, async body => {
                var dual=S(a,"network")=="dual";
                JsonNode created;
                try { created=await Api("servers",body); }
                catch(TimewebApiException ex) when(IsKnownOrderRejection(ex)) {
                    var zone=S(a,"zone");
                    return RejectOrder(zone,selectedLocation,S(body,"preset_id"),ex);
                }
                if(created["server"] is not JsonObject newServer || !int.TryParse(S(newServer,"id"),out var serverId) || serverId<=0)
                    throw new Exception("Заказ отправлен, но ID сервера не получен. Не повторяйте заказ, пока не проверите список серверов.");
                try { return await EnsurePublicNetwork(serverId,dual); }
                catch(Exception ex) { throw new Exception($"VPS #{serverId} уже создан. Публичная сеть не подтверждена: {ex.Message}. Не создавайте новый VPS вместо него; проверьте адреса этого сервера в панели Timeweb."); }
            }); }
            case "ssh.prepare": return await Probe(a,true);
            case "ssh.status": return await Probe(a,false);
            case "ssh.trust": return await Trust(a);
            case "ssh.cloudKey": return await CloudKey(a);
            case "ssh.passwordKey": return await PasswordKey(a);
            case "ssh.terminal": return await OpenTerminal(a);
            case "ssh.test": return await SSH(a,"uname -a; id; uptime",1);
            case "ssh.command": return await SSH(a,S(a,"command"),5);
            case "deploy.preflight": return await DeployPreflight(a);
            case "deploy": return await Deploy(a);
            case "topology.poll": return await TopologyPoll(a);
            case "topology.login": return await TopologyLogin(a);
            case "topology.logout": return await TopologyLogout();
            case "topology.selftest": return TopologySelfTest();
            default: throw new Exception("Неизвестная операция");
        }
    }
    async Task<JsonObject> TimeProfile() {
        if(token.Length==0) return new JsonObject{["connected"]=false};
        try { await Api("servers?limit=1"); } catch when(token.Length==0) { return new JsonObject{["connected"]=false}; }
        var result=new JsonObject{["connected"]=true}; var warnings=new List<string>();
        try { var st=(await Api("account/status"))["status"] as JsonObject; if(st!=null) result["profile"]=Pick(st,"login","is_blocked","is_permanent_blocked"); }
        catch(Exception e) { warnings.Add(e.Message); }
        try { var f=(await Api("account/finances"))["finances"] as JsonObject; if(f!=null) result["finances"]=Pick(f,"balance","total_balance","currency","hourly_cost","hourly_fee","monthly_fee","monthly_cost","hours_left"); }
        catch(Exception e) { warnings.Add(e.Message); }
        if(token.Length==0) return new JsonObject{["connected"]=false};
        result["warning"]=string.Join(" · ",warnings); return result;
    }
    static JsonObject Pick(JsonObject x,params string[] keys) { var o=new JsonObject(); foreach(var k in keys) if(x[k]!=null) o[k]=x[k]!.DeepClone(); return o; }
    static bool Bool(JsonObject x,string k,bool val) => x[k] is JsonValue v && v.TryGetValue<bool>(out var b) && b==val;
    // Catalog membership permits selection, not a guarantee of real-time capacity.
    static string PresetAvailability(JsonObject x) {
        if(new[]{"is_preorder","preorder","is_pre_order","is_sold_out","sold_out","is_disabled","is_unavailable"}.Any(k=>Bool(x,k,true))) return "blocked";
        if(new[]{"is_available","available","in_stock","is_in_stock","is_enabled"}.Any(k=>Bool(x,k,false))) return "blocked";
        var status=S(x,"status").ToLowerInvariant();
        if(status.Length>0 && status is not ("available" or "in_stock" or "active" or "enabled" or "unknown")) return "blocked";
        var description=string.Join(" ",S(x,"description"),S(x,"description_short"),x["tags"]?.ToJsonString()??"");
        if(Regex.IsMatch(description,@"pre[\s_-]*order|sold[\s_-]*out|unavailable|out[\s_-]*of[\s_-]*stock|предзаказ|предварительный заказ|нет в наличии|нет свободных мощностей|ожидается поставка|недоступен|недоступна|недоступно",RegexOptions.IgnoreCase)) return "blocked";
        return new[]{"is_available","available","in_stock","is_in_stock"}.Any(k=>Bool(x,k,true)) || status is "available" or "in_stock" ? "confirmed" : "catalog";
    }
    static object CatalogSelfTest() {
        var cases=new (string Json,string Expected)[]{
            ("{\"id\":1,\"cpu\":2,\"ram\":4096,\"disk\":40960,\"location\":\"ru-1\",\"price\":500,\"tags\":[]}","catalog"),
            ("{\"is_available\":true}","confirmed"),("{\"status\":\"active\"}","catalog"),
            ("{\"is_available\":false}","blocked"),("{\"is_preorder\":true,\"is_available\":true}","blocked"),
            ("{\"tags\":[\"pre-order\"]}","blocked"),("{\"description_short\":\"Предзаказ\"}","blocked"),
            ("{\"status\":\"sold_out\"}","blocked"),("{\"is_enabled\":false}","blocked")};
        foreach(var c in cases) if(PresetAvailability(JsonNode.Parse(c.Json)!.AsObject())!=c.Expected) throw new Exception("Catalog regression: "+c.Json);
        var locationTests=LocationSelfTest();
        return new {passed=cases.Length+locationTests};
    }
    // Provider's published zone matrix, checked 2026-11-24. Unknown zones fail closed.
    static bool DualStackZone(string label) => label.ToLowerInvariant() is
        "spb-1" or "spb-2" or "spb-3" or "spb-4" or "msk-1" or "ams-1" or "ala-1" or "buf-2" or
        "ru-1" or "ru-3" or "nl-1" or "kz-1";
    static bool PublicAddress(string address, string type) {
        if(!IPAddress.TryParse(address,out var ip)) return false;
        var b=ip.GetAddressBytes();
        if(type=="ipv6") return b.Length==16 && (b[0]&0xe0)==0x20 && !(b[0]==0x20 && b[1]==1 && b[2]==0x0d && b[3]==0xb8);
        if(type!="ipv4" || b.Length!=4) return false;
        return b[0] is >0 and <224 && b[0]!=10 && b[0]!=127 &&
            !(b[0]==100 && b[1]>=64 && b[1]<=127) && !(b[0]==169 && b[1]==254) &&
            !(b[0]==172 && b[1]>=16 && b[1]<=31) && !(b[0]==192 && b[1]==168) &&
            !(b[0]==192 && b[1]==0 && (b[2]==0 || b[2]==2)) && !(b[0]==198 && (b[1]==18 || b[1]==19 || (b[1]==51 && b[2]==100))) &&
            !(b[0]==203 && b[1]==0 && b[2]==113);
    }
    async Task<JsonArray> PublicIps(int id) {
        var result=await Api($"servers/{id}/ips");
        if(result["server_ips"] is not JsonArray ips) throw new Exception("Неизвестная структура списка IP; дополнительные адреса не заказаны.");
        return ips;
    }
    static string FindPublic(JsonArray ips,string type) => ips.OfType<JsonObject>()
        .Where(x=>S(x,"type")==type && PublicAddress(S(x,"ip"),type)).Select(x=>S(x,"ip")).FirstOrDefault()??"";
    async Task<object> EnsurePublicNetwork(int id,bool dual) {
        var path=$"servers/{id}/ips";
        // Wait for initial provisioning before deciding which addresses are absent.
        var until=DateTime.UtcNow.AddMinutes(6);
        JsonArray? ips=null; int stable=0;
        while(DateTime.UtcNow<until) {
            var info=await Api($"servers/{id}");
            if(info["server"] is not JsonObject server) throw new Exception("Не удалось получить состояние VPS.");
            var status=S(server,"status").ToLowerInvariant();
            if(status is "on" or "off" or "running" or "active" or "stopped") {
                ips=await PublicIps(id);
                if(FindPublic(ips,"ipv4")!="" && (!dual || FindPublic(ips,"ipv6")!="")) return new {serverId=id,ipv4=FindPublic(ips,"ipv4"),ipv6=FindPublic(ips,"ipv6"),networkReady=true};
                if(++stable>=3) break;
            } else stable=0;
            await Task.Delay(5000);
        }
        if(stable<3 || ips==null) throw new Exception("VPS ещё устанавливается; адреса не удалось подтвердить до тайм-аута.");
        foreach(var type in (dual ? new[]{"ipv4","ipv6"} : new[]{"ipv4"})) {
            ips=await PublicIps(id);
            if(FindPublic(ips,type)!="") continue;
            // Never retry a potentially billed POST automatically, even if its response is lost.
            await Api(path,new {type});
            var ipDeadline=DateTime.UtcNow.AddMinutes(2);
            do {
                await Task.Delay(3000); ips=await PublicIps(id);
                if(FindPublic(ips,type)!="") break;
            } while(DateTime.UtcNow<ipDeadline);
            if(FindPublic(ips,type)=="") throw new Exception($"Запрошен {type}, но назначение ещё не подтверждено.");
        }
        ips=await PublicIps(id);
        var ipv4=FindPublic(ips,"ipv4"); var ipv6=FindPublic(ips,"ipv6");
        if(ipv4==""||(dual&&ipv6=="")) throw new Exception("Не подтверждены требуемые публичные адреса.");
        return new {serverId=id,ipv4,ipv6,networkReady=true};
    }
    static bool Positive(JsonObject x,string k) => decimal.TryParse(S(x,k),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out var n)&&n>0;
    async Task<JsonObject> Catalog() {
        var pr=await Api("presets/servers"); var os=await Api("os/servers");
        // The preset catalog alone does not establish location eligibility or stock.
        var locations=ReadLocationMap(await Api("locations",version:2));
        if(pr["server_presets"] is not JsonArray raw || os["servers_os"] is not JsonArray systems) throw new Exception("Неизвестная структура каталога Timeweb.");
        var plans=new JsonArray(); var osList=new JsonArray(); int locationFiltered=0;
        foreach(var x in raw.OfType<JsonObject>()) {
            var availability=PresetAvailability(x);
            if(availability=="blocked"||!Positive(x,"id")||!Positive(x,"cpu")||!Positive(x,"ram")||!Positive(x,"disk")) continue;
            if(Positive(x,"gpu")||S(x,"preset_type").Contains("gpu",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(x.ToJsonString(), @"arm64|aarch64", RegexOptions.IgnoreCase)) continue;
            var zone=S(x,"availability_zone"); var location=S(x,"location"); var label=zone.Length>0?zone:location; if(label.Length==0) continue;
            if(!Regex.IsMatch(label,@"^[A-Za-z0-9_-]+$")) continue;
            if(!LocationListed(locations,location,zone)) { locationFiltered++; continue; }
            if(IsCapacityBlocked(label,location,S(x,"id"))) continue;
            var plan=Pick(x,"id","cpu","ram","disk","disk_type","preset_type","price","description_short");
            plan["availability"]=availability; plan["dualStack"]=DualStackZone(label); plan["zone"]=label; plan["location"]=location; plan["availabilityZone"]=zone;
            // Only explicit hour prices; monthly price is displayed separately, never silently converted.
            var hour=new[]{"hourly_price","price_hour","price_per_hour","hourly_cost"}.FirstOrDefault(k=>Positive(x,k));
            if(hour!=null) plan["hourly"]=x[hour]!.DeepClone();
            if(!Positive(plan,"price")&&!Positive(plan,"hourly")) continue;
            plans.Add(plan);
        }
        foreach(var x in systems.OfType<JsonObject>()) {
            if(!Positive(x,"id")||!S(x,"name").Contains("ubuntu",StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(S(x,"version")+" "+S(x,"name"),@"\b24\.04\b")) continue;
            if(Regex.IsMatch(x.ToJsonString(),@"arm64|aarch64",RegexOptions.IgnoreCase)||Bool(x,"is_available",false)) continue;
            osList.Add(Pick(x,"id","name","version"));
        }
        return new JsonObject{["plans"]=plans,["systems"]=osList,["totalPresets"]=raw.Count,["locationFiltered"]=locationFiltered,["locations"]=System.Text.Json.JsonSerializer.SerializeToNode(locations.OrderBy(kv=>kv.Key).Select(kv=>new {location=kv.Key,zones=kv.Value.Order().ToArray()})),["capacityBlocks"]=System.Text.Json.JsonSerializer.SerializeToNode(CapacityBlocks()),["sampleFields"]=new JsonArray(raw.OfType<JsonObject>().SelectMany(x=>x.Select(k=>k.Key)).Distinct().Order().Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())};
    }
    // Recheck exact server ID and public address, then resolve the supplied DNS name.
    async Task<JsonObject> DeployPreflight(JsonObject a) {
        var id=Num(a,"serverId");
        var host=Need(a,"host",@"^[0-9.]+$");
        if(!PublicAddress(host,"ipv4")) throw new Exception("Требуется публичный IPv4.");
        var domain=Need(a,"domain",@"^(?=.{1,253}$)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$");
        var server=(await Api("servers/"+id))["server"] as JsonObject ?? throw new Exception("VPS не найден в аккаунте Timeweb.");
        JsonObject Pending(string message)=>new(){["ready"]=false,["message"]=message};
        if(S(server,"status")!="on") return Pending("Ожидаем включения VPS #"+id+" · "+S(server,"status"));
        var ips=await PublicIps(id);
        if(!ips.OfType<JsonObject>().Any(x=>S(x,"type")=="ipv4"&&S(x,"ip")==host)) return Pending("Публичный IPv4 больше не подтверждён для этого VPS.");
        if(S(a,"network")=="dual" && S(a,"requireDual")=="true" && FindPublic(ips,"ipv6")=="") return Pending("Ожидаем публичный IPv6.");
        if(S(a,"dnsZone")!="") {
            var root=Need(a,"dnsZone",DnsNamePattern);
            var records=await DnsRecords(root,domain);
            var dual=S(a,"network")=="dual";
            var ipv6=dual?FindPublic(ips,"ipv6"):"";
            CheckDnsConflict(records,host,ipv6);
            if(!records.Any(r=>r.type=="A"&&SameIp(r.value,host))||dual&&!records.Any(r=>r.type=="AAAA"&&SameIp(r.value,ipv6)))return Pending("Timeweb API ещё не подтверждает необходимые DNS-записи. Продолжите настройку DNS без нового VPS.");
        }
        var dns=await PublicDnsReady(domain,host,ips,S(a,"network")=="dual");
        if(dns.error!=null)return Pending((S(a,"dnsZone")!=""?"Запись подтверждена Timeweb API. ":"")+dns.error+" "+dns.detail);
        return new JsonObject{["ready"]=true,["message"]="VPS включён, публичный DNS подтверждён. "+dns.detail};
    }
    async Task<object> Deploy(JsonObject a) {
        if(S(a,"confirm")!="DEPLOY") throw new Exception("Подтвердите развёртывание");
        var role=Need(a,"role",@"^(Orchestra|Global)$"); var domain=Need(a,"domain",@"^[A-Za-z0-9](?:[A-Za-z0-9-]*\.)+[A-Za-z]{2,63}$|^[A-Za-z0-9][A-Za-z0-9.-]*\.[A-Za-z]{2,63}$"); if(domain.Contains("..")) throw new Exception("Некорректный домен");
        var orchestra=S(a,"orchestra"); var script=S(a,"script");
        if(role=="Global") { if(!Uri.TryCreate(orchestra,UriKind.Absolute,out var u)||u.Scheme!="https"||u.UserInfo!=""||u.Query!=""||u.Fragment!="") throw new Exception("Нужен HTTPS URL существующего Orchestra"); if(!File.Exists(script)) throw new Exception("Выберите проверенный bash-скрипт Global"); await RequireDeveloper(orchestra); }
        else script=Path.Combine(AppContext.BaseDirectory,"profiles","orchestra-r3.sh");
        if(!File.Exists(script)) throw new Exception("Скрипт установки не найден");
        var binding=await ReserveBinding(a); var check=await DeployPreflight(a); if(!Bool(check,"ready",true)) throw new Exception(S(check,"message"));
        var digest=await GithubReleaseDigest();
        await SSH(a,"test \"$(id -u)\" = 0 && test \"$(uname -m)\" = x86_64 && command -v bash && command -v flock && . /etc/os-release && test \"$ID:$VERSION_ID\" = ubuntu:24.04",1);
        var temp=Directory.CreateTempSubdirectory("mysticego-").FullName; var remote="/root/.clouddesk-"+Guid.NewGuid().ToString("N");
        try {
            await Exec("gh",["release","download",Tag,"--repo",Repo,"--pattern",Asset,"--dir",temp]); var file=Path.Combine(temp,Asset);
            using(var fs=File.OpenRead(file)) { if(Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant()!=digest) throw new Exception("SHA-256 архива релиза не совпадает"); }
            await SSH(a,"umask 077; mkdir -m 700 "+remote,1); await Upload(a,file,remote+"/r3.zip"); await Upload(a,script,remote+"/install.sh");
            static string Q(string s)=>"'"+s.Replace("'","'\"'\"'")+"'";
            var command="test ! -e /opt/mysticego/app && test ! -e /var/lib/mysticego && test ! -e /etc/mysticego/orchestra.env && test ! -e /etc/systemd/system/mysticego-orchestra.service && ! systemctl list-unit-files --no-legend | grep -qi mysticego && bash "+remote+"/install.sh "+Q(remote+"/r3.zip")+" "+Q(domain)+" "+Q(orchestra)+(role=="Orchestra"?" "+Q(digest):"");
            SaveBinding(binding with {Phase="installing"});
            var output=await SSH(a,"flock -n /run/lock/mysticego-clouddesk-deploy.lock bash -c "+Q(command),25);
            SaveBinding(binding with {Phase="installed"}); await SSH(a,"rm -rf -- "+remote,1); return new {role,domain,release=Tag,output};
        } catch(Exception e) { throw new Exception(e.Message+"\nЕсли каталог был создан, диагностика сохранена на сервере: "+remote); }
        finally { Directory.Delete(temp,true); }
    }
}
