using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using MysticEgo.Deployment;

internal sealed class LabHost(Dictionary<string,string> flags)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    private readonly string root=Path.GetFullPath(flags.GetValueOrDefault("root")??Environment.CurrentDirectory);
    private readonly bool joining=flags.ContainsKey("join");
    private readonly int upstreamOffset=int.Parse(flags.GetValueOrDefault("upstream-offset")??"0");
    private readonly List<Process> tunnels=new();
    private readonly int offset=int.Parse(flags.GetValueOrDefault("port-offset")??"0");
    private readonly Dictionary<string,Process> children=new();
    private readonly HttpClient http=new(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(10)};
    private readonly CancellationTokenSource shutdown=new();
    private CancellationToken Ct=>shutdown.Token;
    private string data="";
    private readonly string machine = HostFingerprint.Read();
    private StationEndpoints? endpoints;
    private string? activeRole;
    private readonly string? centerHost = flags.GetValueOrDefault("listen-host") ??
        Environment.GetEnvironmentVariable("MYSTICEGO_CENTER_PUBLIC_HOST");
    private string CenterPublic(string role) => Environment.GetEnvironmentVariable(
        role == "auth" ? "MYSTICEGO_CENTER_AUTH_URL" : "MYSTICEGO_CENTER_ORCHESTRA_URL") ??
        (string.IsNullOrWhiteSpace(centerHost) ? Local(role) : $"http://{centerHost}:{LabPackage.Roles[role].Port + offset}");
    private int Port(string role) => role == activeRole && endpoints is not null && new Uri(endpoints.PublicUrl).Scheme == "http"
        ? new Uri(endpoints.PublicUrl).Port : LabPackage.Roles[role].Port + offset;
    private string Bind(string role) =>
        (role == activeRole && endpoints is not null && new Uri(endpoints.PublicUrl) is { Scheme: "http", IsLoopback: false } ||
            !joining && !string.IsNullOrWhiteSpace(centerHost))
        ? $"http://0.0.0.0:{Port(role)}" : Local(role);
    private string PublicKey=>Path.Combine(data,"public.pem");
    private string Bin=>Path.Combine(root,"runtime","app","bin");
    private string Local(string role) => "http://127.0.0.1:" + Port(role);
    private string Central(string role)
    {
        if (endpoints is null) return joining ? "http://127.0.0.1:" + (LabPackage.Roles[role].Port + upstreamOffset) : Local(role);
        return role switch
        {
            "auth" => endpoints.AuthUrl, "orchestra" => endpoints.OrchestraUrl,
            "global" => endpoints.GlobalUrl ?? "", "local" => endpoints.LocalUrl ?? "", _ => ""
        };
    }
    private string Dotnet=>Environment.GetEnvironmentVariable("MYSTICEGO_DOTNET") ??
        (Path.GetFileNameWithoutExtension(Environment.ProcessPath)=="dotnet"?Environment.ProcessPath!:"dotnet");

    public async Task Run()
    {
        if(offset is <0 or >50000 || upstreamOffset is <0 or >50000)throw new ArgumentException("Недопустимое смещение портов");
        if (!string.IsNullOrWhiteSpace(centerHost) && !Uri.CheckHostName(centerHost).Equals(UriHostNameType.Dns) &&
            !Uri.CheckHostName(centerHost).Equals(UriHostNameType.IPv4))
            throw new ArgumentException("Укажите IPv4 или DNS-имя главного сервера.");
        StationProfile? profile=null;
        if(joining)
        {
            var incoming=flags["join"];
            profile=JsonSerializer.Deserialize<StationProfile>(await File.ReadAllTextAsync(incoming),Json);
            if(profile?.Credentials is not {Length:>0 and <=4} roles ||roles.Any(c=>c.Kind is not ("global" or "audit" or "local" or "agent") || c.IdentityId==Guid.Empty || string.IsNullOrEmpty(c.Secret)) ||
                roles.Select(x=>x.Kind).Distinct().Count()!=roles.Length)throw new InvalidDataException("Некорректный station.json.");
            endpoints = profile.Endpoints;
            if (endpoints is not null)
            {
                if (roles.Length != 1) throw new InvalidDataException("Профиль нового формата содержит ровно одну роль.");
                activeRole = roles[0].Kind;
                foreach (var url in new[] { endpoints.AuthUrl, endpoints.OrchestraUrl, endpoints.PublicUrl, endpoints.LocalUrl, endpoints.GlobalUrl }
                             .Where(url => !string.IsNullOrEmpty(url)))
                    if (!Uri.TryCreate(url, UriKind.Absolute, out var address) || address.UserInfo.Length != 0 ||
                        address.Scheme != "https" && !(address.Scheme == "http" && (address.IsLoopback || endpoints.AllowLanHttp)))
                        throw new InvalidDataException("В профиле найден недопустимый адрес.");
                if (activeRole == "local" && string.IsNullOrEmpty(endpoints.GlobalUrl) ||
                    activeRole == "agent" && string.IsNullOrEmpty(endpoints.LocalUrl))
                    throw new InvalidDataException("В профиле отсутствует родительский узел.");
            }
        }
        data=Path.GetFullPath(flags.GetValueOrDefault("output")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MysticEgo","BETA7-Lab",joining?"station-"+profile!.Credentials[0].IdentityId:"center"));
        Directory.CreateDirectory(data);
        if(!OperatingSystem.IsWindows())File.SetUnixFileMode(data,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        using var instance=new FileStream(Path.Combine(data,"instance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        if (!flags.ContainsKey("no-lm")) File.Delete(Path.Combine(data, "lm-studio-model.txt"));
        Console.CancelKeyPress+=(s,e)=>{e.Cancel=true;shutdown.Cancel();};
        using var sig=OperatingSystem.IsWindows()?null:PosixSignalRegistration.Create(PosixSignal.SIGTERM,c=>{c.Cancel=true;shutdown.Cancel();});
        AppDomain.CurrentDomain.ProcessExit+=(_,_)=>{foreach(var p in children.Values.ToArray())try{if(!p.HasExited)p.Kill(true);}catch{}};
        if (flags.ContainsKey("parent-stdin")) _ = Task.Run(WatchParent);
        Console.WriteLine("Данные и журналы: "+data);
        try
        {
            if (!joining)
            {
                await StartCenter();
                if (!flags.ContainsKey("no-lm")) _ = SetupModel("orchestra");
                await WatchCore();
            }
            else
            {
                if(File.Exists(PublicKey)&&File.ReadAllText(PublicKey)!=profile!.PublicKey)throw new InvalidDataException("Ключ Auth отличается от ранее сохранённого.");
                WritePrivate(PublicKey,profile!.PublicKey);
                if(flags.TryGetValue("ssh",out var host))
                {
                    var forwards=new[]{"auth","orchestra","global","local"}.Except(profile.Credentials.Select(c=>c.Kind));
                    foreach(var group in forwards.GroupBy(role=>role switch{
                        "local" when !string.IsNullOrWhiteSpace(profile.LocalSsh)=>profile.LocalSsh!,
                        "global" when !string.IsNullOrWhiteSpace(profile.GlobalSsh)=>profile.GlobalSsh!,
                        _=>host
                    })) tunnels.Add(OpenTunnel(group.Key,group));
                }
                await Supervise(profile!);
            }
        }
        finally
        {
            shutdown.Cancel();
            foreach(var role in children.Keys.Reverse().ToArray())await Stop(role);
            foreach(var tunnel in tunnels){if(!tunnel.HasExited)tunnel.Kill(true);tunnel.Dispose();}
            http.Dispose();
        }
    }
    private async Task WatchParent()
    {
        try
        {
            while (await Console.In.ReadLineAsync(Ct) is string line)
                if (line == "stop") break;
            shutdown.Cancel();
        }
        catch (OperationCanceledException) { }
        catch (IOException) { shutdown.Cancel(); }
    }
    private static void WritePrivate(string path,string text)
    {
        var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(tmp,text);InstallerProfiles.Restrict(tmp);File.Move(tmp,path,true);
    }
    private async Task StartCenter()
    {
        var signing = Path.Combine(data, "private.pem");
        InstallerProfiles.WriteSigningKeys("production", signing, PublicKey);
        var passwordPath = Path.Combine(data, "owner-password.txt");
        if (!File.Exists(passwordPath)) WritePrivate(passwordPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(20)));
        var avatarPath = Path.Combine(data, "avatar-key.txt");
        if (!File.Exists(avatarPath)) WritePrivate(avatarPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        var ownerPassword = File.ReadAllText(passwordPath).Trim();
        var sqlite = Path.Combine(data, "auth.db");
        var external = Path.Combine(data, "auth-postgres.connection");
        if (File.Exists(external) && File.Exists(sqlite))
            throw new InvalidOperationException("Auth уже использует SQLite. Сначала выполните отдельную миграцию данных.");
        var authConnection = File.Exists(external) ? File.ReadAllText(external).Trim() : "Data Source=" + sqlite;
        if (File.Exists(external) && !authConnection.StartsWith("Host=", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Ожидается строка подключения PostgreSQL для Auth.");
        await Start("auth", new()
        {
            ["Urls__Http"] = Bind("auth"), ["Authorization__ConnectionString"] = authConnection,
            ["Authorization__Jwt__PrivateKeyPath"] = signing, ["Authorization__Bootstrap__Login"] = "developer",
            ["Authorization__Bootstrap__Password"] = ownerPassword
        });
        var centerIdFile = Path.Combine(data, "center-id.txt");
        if (!File.Exists(centerIdFile)) WritePrivate(centerIdFile, Guid.NewGuid().ToString());
        var identityPath = Path.Combine(data, "orchestra-identity.json");
        if (!File.Exists(identityPath))
        {
            using var login = await http.PostAsJsonAsync(Local("auth") + "/api/auth/login", new { login = "developer", password = ownerPassword }, Ct);
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString();
            using var request = new HttpRequestMessage(HttpMethod.Post, Local("auth") + "/api/identities/provision")
            {
                Content = JsonContent.Create(new { name = "Orchestra deployment identity", kind = "orchestra" })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await http.SendAsync(request, Ct);
            response.EnsureSuccessStatusCode();
            WritePrivate(identityPath, await response.Content.ReadAsStringAsync(Ct));
        }
        var identity = JsonSerializer.Deserialize<StationCredential>(await File.ReadAllTextAsync(identityPath, Ct), Json)!;
        var lan = Environment.GetEnvironmentVariable("MYSTICEGO_ALLOW_LAN_HTTP") == "true";
        await Start("orchestra", new()
        {
            ["Node__NodeId"] = File.ReadAllText(centerIdFile).Trim(), ["Node__IdentityId"] = identity.IdentityId.ToString(),
            ["Node__IdentitySecret"] = identity.Secret, ["Avatar__SyncKey"] = File.ReadAllText(avatarPath),
            ["Node__PublicUrl"] = CenterPublic("orchestra"), ["Node__BrowserAuthUrl"] = CenterPublic("auth"),
            ["Node__AllowInsecureTestHttp"] = lan.ToString(), ["Managed__AllowLanHttp"] = lan.ToString(),
            ["Managed__PackageRoot"] = root, ["Node__MachineFingerprint"] = machine, ["Soul__StoragePath"] = Path.Combine(data, "souls")
        });
        Console.WriteLine($"Оркестр: {Local("orchestra")}/");
        Console.WriteLine($"Портал Audit: {Local("orchestra")}/audit-portal/");
        Console.WriteLine($"Логин: developer, файл пароля: {passwordPath}");
        if (OperatingSystem.IsWindows() && !flags.ContainsKey("no-browser"))
        {
            try { using var browser = Process.Start(new ProcessStartInfo(Local("orchestra")) { UseShellExecute = true }); }
            catch (System.ComponentModel.Win32Exception) { Console.WriteLine("Откройте адрес Оркестра в браузере."); }
        }
    }

    private async Task WatchCore()
    {
        while (!Ct.IsCancellationRequested)
        {
            foreach (var (role, child) in children)
                if (child.HasExited) throw new IOException($"Остановился {role}. См. журналы в {data}.");
            await Task.Delay(TimeSpan.FromSeconds(2), Ct);
        }
    }

    private async Task Supervise(StationProfile profile)
    {
        var credential=profile.Credentials[0];
        string? token=null,error=null;var refresh=DateTimeOffset.MinValue;
        var healthy=Array.Empty<string>();bool lmStarted=false,readyPrinted=false;
        StationLink? link = null;
        string? lastNotice = null;
        var updates = activeRole == "agent" && endpoints is not null ? new AgentStationUpdates(data, endpoints) : null;
        var synced = false;
        while(!Ct.IsCancellationRequested)
        {
            if(tunnels.Any(t=>t.HasExited))throw new IOException("SSH отключён. Проверьте имя сервера, отпечаток, доступ и порты. Повторите запуск с тем же station.json.");
            foreach(var role in new[]{"auth","orchestra"})
                if(children.TryGetValue(role,out var core)&&core.HasExited)throw new IOException("Остановился "+role+". См. журналы в "+data);
            try
            {
                if(DateTimeOffset.UtcNow>=refresh)
                {
                    using var issued=await http.PostAsJsonAsync(Central("auth")+"/api/identities/token",new{credential.IdentityId,credential.Secret},Ct);
                    issued.EnsureSuccessStatusCode();
                    var body=await issued.Content.ReadFromJsonAsync<JsonElement>(Ct);
                    token=body.GetProperty("accessToken").GetString();refresh=body.GetProperty("expiresAt").GetDateTimeOffset().AddMinutes(-1);
                }
                if (!synced && endpoints is not null)
                {
                    using var configRequest = new HttpRequestMessage(HttpMethod.Get, Central("orchestra").TrimEnd('/') + "/api/v1/managed/configuration");
                    configRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    using var configuration = await http.SendAsync(configRequest, Ct);
                    if (configuration.IsSuccessStatusCode)
                    {
                        var refreshed = await configuration.Content.ReadFromJsonAsync<StationProfile>(Ct);
                        if (refreshed?.Endpoints is { } links && refreshed.PublicKey == profile.PublicKey &&
                            refreshed.Credentials.SingleOrDefault()?.IdentityId == credential.IdentityId &&
                            links.OrchestraUrl == endpoints.OrchestraUrl && links.PublicUrl == endpoints.PublicUrl)
                        {
                            endpoints = links;
                            if (activeRole == "agent") updates = new AgentStationUpdates(data, endpoints);
                        }
                    }
                    synced = true;
                }
                using var req=new HttpRequestMessage(HttpMethod.Post,Central("orchestra")+"/api/v1/stations/check-in"){
                    Content=JsonContent.Create(new StationReport(healthy,error,link,machine))};
                req.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
                using var result=await http.SendAsync(req,Ct);
                result.EnsureSuccessStatusCode();
                var decision=await result.Content.ReadFromJsonAsync<StationDecision>(Ct);
                error = decision?.Reason;
                if (decision?.Reason is { } reason)
                {
                    if (lastNotice != reason) Console.Error.WriteLine(reason);
                    lastNotice = reason;
                }
                else lastNotice = null;
                if (endpoints is not null && (link is null || DateTimeOffset.UtcNow - link.MeasuredAt > TimeSpan.FromSeconds(45)))
                    link = await MeasureParent();
                var working=new List<string>();
                foreach(var c in profile.Credentials)
                {
                    try
                    {
                        if(!decision!.Enabled){await Stop(c.Kind);continue;}
                        if(children.TryGetValue(c.Kind,out var p)&&p.HasExited)await Stop(c.Kind);
                        var update = c.Kind == "agent" ? await PrepareUpdate(updates) : null;
                        if(update is not null) await Stop(c.Kind);
                        if(!children.ContainsKey(c.Kind))
                        {
                            var settings=new Dictionary<string,string>{
                                ["Node__DisplayName"]=profile.Name+" / "+c.Kind,["Node__IdentityId"]=c.IdentityId.ToString(),
                                ["Node__IdentitySecret"]=c.Secret,["Node__NodeId"]=c.IdentityId.ToString(),
                                ["Avatar__SyncKey"]=profile.AvatarKey,["Avatar__AllowInsecureLocalSync"]="true",["Avatar__AllowInsecureLanSync"]=(endpoints?.AllowLanHttp == true).ToString(),
                                ["MYSTICEGO_AGENT_OWNER_USER"]=endpoints?.OwnerLogin ?? Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER") ?? "developer",
                                ["Node__Managed"]=(endpoints is not null).ToString(),["Node__ParentId"]=endpoints?.ParentId??"",
                                ["Node__BrowserAuthUrl"]=endpoints?.AuthUrl??Central("auth"),["Node__PublicUrl"]=endpoints?.PublicUrl??Local(c.Kind),
                                ["Node__AllowInsecureTestHttp"]=(endpoints?.AllowLanHttp ?? false).ToString(),
                                ["Node__OrchestraUrl"]=Central("orchestra"),["Node__GlobalUrl"]=Upstream("global"),
                                ["Node__LocalUrl"]=Upstream("local"),["Node__DeploymentMode"]=joining?"remote-distributed":"single-machine"
                            };
                            if (endpoints is not null)
                            {
                                settings["Node__GlobalId"] = endpoints.GlobalId ?? "";
                                settings["Node__LocalId"] = endpoints.LocalId ?? "";
                                settings["Node__ClubAddress"] = endpoints.ClubAddress ?? "";
                                settings["Node__ClubDescription"] = endpoints.ClubDescription ?? "";
                                settings["Node__MachineFingerprint"] = machine;
                                if (c.Kind is "global" or "local" && endpoints.ReleasePrivateKey is { Length: > 0 } key)
                                {
                                    var keyPath = Path.Combine(data, "release-signing.pem");
                                    if (!File.Exists(keyPath)) WritePrivate(keyPath, key);
                                    settings["AgentUpdates__PrivateKeyPath"] = keyPath;
                                }
                                if (c.Kind == "agent")
                                {
                                    WritePrivate(Path.Combine(data, "global-release.pem"), endpoints.GlobalReleaseKey ?? "");
                                    WritePrivate(Path.Combine(data, "local-release.pem"), endpoints.LocalReleaseKey ?? "");
                                    settings["AgentUpdates__PublicKeyPath"] = Path.Combine(data, "global-release.pem");
                                    settings["AgentUpdates__LocalPublicKeyPath"] = Path.Combine(data, "local-release.pem");
                                    settings["AgentUpdates__Enabled"] = "true";
                                    settings["AgentUpdates__AutoInstall"] = "true";
                                    settings["AgentUpdates__PollMinutes"] = "2";
                                }
                            }
                            if(c.Kind is "local" or "agent") settings["Avatar__UpstreamUrl"]=Upstream(c.Kind=="local"?"global":"local");
                            var installed = update ?? updates?.Active;
                            if (installed is not null) settings["AgentUpdates__CurrentVersion"] = installed.Version;
                            else if (c.Kind == "agent") settings["AgentUpdates__CurrentVersion"] = "0.8.0";
                            try
                            {
                                await Start(c.Kind, settings, installed?.Folder);
                                if (update is not null)
                                {
                                    updates!.Saved(update);
                                    Console.WriteLine("Установлен подписанный Agent " + update.Version);
                                }
                            }
                            catch (IOException) when (update is not null && !Ct.IsCancellationRequested)
                            {
                                updates!.Rejected(update);
                                var previous = updates.Active;
                                settings["AgentUpdates__CurrentVersion"] = previous?.Version ?? "0.8.0";
                                await Start(c.Kind, settings, previous?.Folder);
                                Console.Error.WriteLine("Новый Agent не запустился. Восстановлена прежняя версия.");
                            }
                        }
                        if(await Healthy(c.Kind,c.IdentityId.ToString()))working.Add(c.Kind);
                    }
                    catch(Exception e) when(!Ct.IsCancellationRequested){error=e.Message;Console.Error.WriteLine(error);}
                }
                healthy=working.ToArray();
                if(healthy.Length==profile.Credentials.Length && !readyPrinted){
                    Console.WriteLine("Работают: "+string.Join(", ",healthy)+". Ctrl+C завершает этот сеанс.");readyPrinted=true;
                }
                if(!lmStarted && !flags.ContainsKey("no-lm") && healthy.Length > 0){
                    lmStarted=true;_ = SetupModel(healthy[0]);
                }
            }
            catch(Exception e) when(!Ct.IsCancellationRequested){
                if(e is HttpRequestException {StatusCode:System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized}){
                    foreach(var c in profile.Credentials)await Stop(c.Kind);
                    healthy=[];refresh=DateTimeOffset.MinValue;
                }
                Console.Error.WriteLine("Связь с Auth/Orchestra: "+e.Message);
            }
            await Task.Delay(TimeSpan.FromSeconds(4),Ct);
        }
        string Upstream(string role)=>profile.Credentials.Any(c=>c.Kind==role)?Local(role):Central(role);
    }
    private async Task<AgentStationUpdates.Release?> PrepareUpdate(AgentStationUpdates? updates)
    {
        if (updates is null) return null;
        try { return await updates.Prepare(Ct); }
        catch (Exception error) when (error is IOException or InvalidDataException or JsonException)
        { Console.Error.WriteLine("Монтаж обновления Agent: " + error.Message); return null; }
    }

    private async Task<StationLink> MeasureParent()
    {
        var parent = activeRole switch
        {
            "global" or "audit" => endpoints!.OrchestraUrl,
            "local" => endpoints!.GlobalUrl,
            "agent" => endpoints!.LocalUrl,
            _ => null
        };
        if (string.IsNullOrEmpty(parent)) return new(null, null, DateTimeOffset.UtcNow);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        limit.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            var watch = Stopwatch.StartNew();
            using var response = await http.GetAsync(parent.TrimEnd('/') + "/health/live", HttpCompletionOption.ResponseHeadersRead, limit.Token);
            response.EnsureSuccessStatusCode();
            var latency = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            var bytes = await http.GetByteArrayAsync(parent.TrimEnd('/') + "/health/link", limit.Token);
            return new(latency, bytes.Length * 8 / Math.Max(.001, watch.Elapsed.TotalSeconds) / 1_000_000, DateTimeOffset.UtcNow);
        }
        catch (Exception ex) when (ex is HttpRequestException || ex is OperationCanceledException && !Ct.IsCancellationRequested)
        { return new(null, null, DateTimeOffset.UtcNow); }
    }

    private Process OpenTunnel(string host,IEnumerable<string> roles)
    {
        if(!System.Text.RegularExpressions.Regex.IsMatch(host,@"^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$"))
            throw new ArgumentException("--ssh ожидает user@host");
        int port=int.Parse(flags.GetValueOrDefault("ssh-port")??"22");
        if(port is <1 or >65535)throw new ArgumentException("Недопустимый порт SSH");
        var start=new ProcessStartInfo("ssh"){UseShellExecute=false};
        foreach(var arg in new[]{"-N","-o","ExitOnForwardFailure=yes","-o","ServerAliveInterval=15","-p",port.ToString()})start.ArgumentList.Add(arg);
        if(flags.TryGetValue("ssh-config",out var cfg)){start.ArgumentList.Add("-F");start.ArgumentList.Add(Path.GetFullPath(cfg));}
        foreach(var role in roles)
        {
            var p=LabPackage.Roles[role].Port;
            start.ArgumentList.Add("-L");start.ArgumentList.Add($"127.0.0.1:{p+upstreamOffset}:127.0.0.1:{p}");
        }
        start.ArgumentList.Add(host);
        return Process.Start(start)??throw new IOException("Не удалось запустить OpenSSH Client.");
    }
    private async Task SetupModel(string role)
    {
        try
        {
            var model = await LabModel.Setup(Ct, role);
            var file = Path.Combine(data, "lm-studio-model.txt");
            WritePrivate(file + ".tmp", model);
            File.Move(file + ".tmp", file, true);
        }
        catch (OperationCanceledException) when (Ct.IsCancellationRequested) { }
        catch (Exception error) { Console.Error.WriteLine("LM Studio: ошибка подготовки. " + error.Message); }
    }
    private async Task Start(string role,Dictionary<string,string> settings,string? binaryDirectory = null)
    {
        var (project,_) = LabPackage.Roles[role];
        var port = Port(role);
        using(var check=new TcpListener(System.Net.IPAddress.Loopback,port)){
            try{check.Start();}catch(SocketException){throw new IOException($"Порт {port} занят. Роль {role} не запущена.");}
        }
        var dir=Path.Combine(data,role);Directory.CreateDirectory(dir);
        var config=Path.Combine(root,"runtime","app","settings",role+".json");
        File.Copy(config,Path.Combine(dir,"appsettings.json"),true);
        var start=new ProcessStartInfo(Dotnet){UseShellExecute=false,WorkingDirectory=dir,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
            StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
        start.ArgumentList.Add(Path.Combine(binaryDirectory ?? Bin,project+".dll"));
        var env=start.Environment;
        if (role is "agent" or "audit")
            env["Vision__AssetDirectory"] = Path.Combine(root, "runtime", "vision");
        if (role is "agent" or "audit" or "orchestra")
            env["Avatar__BundledDirectory"] = Path.Combine(root, "runtime", "avatars");
        env["ASPNETCORE_URLS"]=Bind(role);env["ASPNETCORE_CONTENTROOT"]=dir;
        if(role!="auth")env["ASPNETCORE_WEBROOT"]=Path.Combine(root,"runtime","app","web",role);
        env["Authorization__Jwt__PublicKeyPath"]=PublicKey;
        env["Node__DataPath"]=Path.Combine(dir,"node.db");env["Node__AuthUrl"]=Central("auth");
        env["Node__PublicUrl"]=Local(role);
        env["Soul__StoragePath"]=Path.Combine(dir,"souls");
        env["AgentUpdates__StagingPath"]=Path.Combine(dir,"update-staging");
        env["AgentUpdates__ReleasesPath"]=Path.Combine(dir,"releases");
        env["WorldAI__DiscoverLocalModel"]="true";
        env["WorldAI__ModelRole"]=role;
        if (!flags.ContainsKey("no-lm")) env["LmStudio__VerifiedModelFile"] = Path.Combine(data, "lm-studio-model.txt");
        env["WorldAI__StoragePath"]=Path.Combine(dir,"world-ai");
        env["Avatar__StoragePath"]=Path.Combine(dir,"avatars");env["Avatar__ReplicaPath"]=Path.Combine(dir,"replicas");
        env["Managed__PackageRoot"]=root;
        env["Managed__AuditWebRoot"]=Path.Combine(root,"runtime","app","web","audit");
        env["Orchestra__NativePlansPath"]=Path.Combine(dir,"plans");env["Orchestra__NativeJobsPath"]=Path.Combine(dir,"jobs");
        env["Logging__LogLevel__Default"]="Warning";
        env["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"]="Warning";
        var origins=new[]{5100,5200,5300,5350,5500}.SelectMany(p=>new[]{$"http://127.0.0.1:{p+offset}",$"http://localhost:{p+offset}"});
        int i=0;foreach(var origin in origins)env["AllowedOrigins__"+i++]=origin;
        foreach(var kv in settings)env[kv.Key]=kv.Value;
        var log=Path.Combine(dir,"service.log");
        if(File.Exists(log)&&new FileInfo(log).Length>2_000_000)File.Move(log,log+".1",true);
        var process=new Process{StartInfo=start};
        var logLock=new object();
        void Line(object sender,DataReceivedEventArgs e){if(e.Data!=null)lock(logLock){try{File.AppendAllText(log,e.Data+Environment.NewLine);}catch(IOException){}}}
        process.OutputDataReceived+=Line;process.ErrorDataReceived+=Line;
        process.Start();children[role]=process;process.BeginOutputReadLine();process.BeginErrorReadLine();
        try
        {
            for(int n=0;n<120 && !process.HasExited;n++){
                if(await Healthy(role,settings.GetValueOrDefault("Node__NodeId")))return;
                await Task.Delay(250,Ct);
            }
            throw new IOException($"Сбой запуска {role}. Журнал: {log}");
        }
        catch{await Stop(role);throw;}
    }
    private async Task<bool> Healthy(string role,string? expectedId)
    {
        try
        {
            using var r=await http.GetAsync(Local(role)+(role=="auth"?"/health":"/health/live"),Ct);
            if(!r.IsSuccessStatusCode)return false;
            var body=await r.Content.ReadFromJsonAsync<JsonElement>(Ct);
            return role=="auth"?body.GetProperty("service").GetString()=="MysticEgo.Authorization.Service":
                body.GetProperty("node").GetString()==expectedId;
        }
        catch(Exception ex) when(!Ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException){return false;}
    }
    private async Task Stop(string role)
    {
        if(!children.Remove(role,out var child))return;
        try{if(!child.HasExited)child.Kill(true);await child.WaitForExitAsync();}
        finally{child.Dispose();}
    }
}
