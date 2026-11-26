using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Docker-free deployment using THE SAME installer. No Python, additional installer or Docker runtime.
internal static class NativeDeployment
{
    static readonly Dictionary<string,(string Project,string Executable,int Port)> Services = new(StringComparer.OrdinalIgnoreCase)
    {
        ["auth"]=("core/Api.Auth/Api.Auth.csproj","Api.Auth",5400),
        ["orchestra"]=("services/Api.Orchestra/Api.Orchestra.csproj","Api.Orchestra",5500),
        ["global"]=("services/Api.Global/Api.Global.csproj","Api.Global",5100),
        ["local"]=("services/Api.Local/Api.Local.csproj","Api.Local",5200),
        ["agent"]=("services/Api.Agent/Api.Agent.csproj","Api.Agent",5300),
        ["audit"]=("services/Api.AuditOrchestra/Api.AuditOrchestra.csproj","Api.AuditOrchestra",5350)
    };
    static readonly HashSet<string> Supported = new(Services.Keys.Append("desktop"),StringComparer.OrdinalIgnoreCase);
    static readonly Regex SshTarget = new(@"^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$",RegexOptions.Compiled);
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web){WriteIndented=true};

    internal static async Task<int> RunAsync(Dictionary<string,string> f)
    {
        if(f.TryGetValue("plan-id",out var planId))return await OrchestraPlanAsync(planId,f);
        if(f.TryGetValue("manifest",out var path))return await ClusterAsync(path,f);
        var root=Path.GetFullPath(f.GetValueOrDefault("root")??FindRoot());
        var roles=(f.GetValueOrDefault("roles")??"auth,orchestra").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries)
            .Select(x=>x.ToLowerInvariant()).Distinct().OrderBy(RoleRank).ToArray();
        if(roles.Length==0 || roles.Any(x=>!Supported.Contains(x)))return Error("Native mode supports auth, orchestra, global, local, agent, audit and desktop; VPN 3x-ui native deployment is not yet supported.");
        string profile;
        try { profile=InstallerProfiles.Get(f); InstallerProfiles.ValidateTestScope(profile,f); }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException) {return Error(e.Message);}
        var outDir=Path.GetFullPath(f.GetValueOrDefault("output")??Path.Combine(root,profile=="test"?"deployment-native-test":"deployment-native"));
        try {InstallerProfiles.ValidateTarget(outDir,profile);}
        catch(InvalidOperationException e){return Error(e.Message);}
        try {CheckDependencies(roles,f);}
        catch(ArgumentException e){return Error(e.Message);}
        if(!File.Exists(Path.Combine(root,"MysticEgo.Distributed.slnx")))return Error("Invalid source root: missing solution.");
        if(!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())return Error("Native services require Windows or Linux.");
        // Preflight is read-only; external prerequisites are checked before any filesystem change.
        if(f.ContainsKey("validate-only")||f.ContainsKey("dry-run"))
        {Console.WriteLine($"Native topology valid: {string.Join(',',roles)} -> {outDir}; no changes made.");return 0;}
        // The single self-contained installer downloads verified official toolchains if missing.
        // --preflight-only remains read-only; --no-auto-deps opts out of downloads.
        var needsNode = !f.ContainsKey("no-build") && roles.Any(x=>x!="auth");
        if(!await InstallerDependencies.EnsureAsync(
            sdk:true,
            node:needsNode,
            installMissing:!f.ContainsKey("preflight-only") && !f.ContainsKey("no-auto-deps")))
            return Error("Missing build prerequisites; see [Prerequisites] above.");
        if(!await VerifyNativeUpstreams(f))return Error("External dependency health/type verification failed. No installation was attempted.");
        string? bootstrapPassword=null;
        if(roles.Contains("auth"))
        {
            try
            {
                var currentAuthEnv=Path.Combine(outDir,"apps","auth","service.env");
                var existing=File.Exists(currentAuthEnv) ? File.ReadAllLines(currentAuthEnv)
                    .FirstOrDefault(x=>x.StartsWith("Authorization__Bootstrap__Password=",StringComparison.Ordinal))?
                    .Split('=',2)[1] : null;
                bootstrapPassword=InstallerProfiles.ResolveBootstrapPassword(profile,existing);
            }
            catch(Exception e) when(e is ArgumentException or InvalidOperationException){return Error(e.Message);}
        }
        if(!roles.Contains("auth") && roles.Any(x=>x!="desktop"))
        {
            var provisioned=Path.Combine(outDir,"secrets","public.pem");
            var incoming=Environment.GetEnvironmentVariable("MYSTICEGO_AUTH_PUBLIC_KEY_FILE");
            if(!File.Exists(provisioned) && (string.IsNullOrWhiteSpace(incoming)||!File.Exists(incoming)))
                return Error("Missing trusted Auth public key: provision MYSTICEGO_AUTH_PUBLIC_KEY_FILE before native installation.");
        }
        if(f.ContainsKey("preflight-only"))
        {Console.WriteLine($"Native preflight passed: {string.Join(',',roles)}; no changes made.");return 0;}
        if(f.ContainsKey("install-services") && roles.Any(x=>x!="desktop") && !IsAdmin())return Error("Installing system services requires Administrator/root permissions. Use --config-only to stage binaries without registration.");
        Directory.CreateDirectory(outDir);
        InstallerProfiles.Mark(outDir,profile);
        Directory.CreateDirectory(Path.Combine(outDir,"data"));
        if(!f.ContainsKey("no-build") && roles.Any(x=>x!="auth"))
        {
            var frontDir=Path.Combine(root,"frontend");
            // npm was checked and, if necessary, bootstrapped before touching deployment files.
            if(await Command("npm",["install"],frontDir)!=0 || await Command("npm",["run","build"],frontDir)!=0)
                return Error("Frontend build failed. Existing services were not modified.");
            foreach(var role in roles.Where(x=>x!="auth"))
            {
                var frontend=Path.Combine(frontDir,"apps",role,"dist");
                var name=role=="audit"?"AuditOrchestra":char.ToUpperInvariant(role[0])+role[1..];
                var wwwroot=Path.Combine(root,"services","Api."+name,"wwwroot");
                if(Directory.Exists(frontend))CopyTree(frontend,wwwroot);
            }
        }
        if(roles.Contains("desktop"))
        {
            if(!f.ContainsKey("no-build"))
            {
                var desktop=Path.Combine(root,"desktop");
                if(await Command("npm",["install"],desktop)!=0||await Command("npm",["run","build"],desktop)!=0)
                    return Error("Desktop Companion build failed.");
                var bundle=Path.Combine(outDir,"desktop");CopyTree(Path.Combine(desktop,"dist"),Path.Combine(bundle,"dist"));
                File.Copy(Path.Combine(desktop,"main.mjs"),Path.Combine(bundle,"main.mjs"),true);
                File.Copy(Path.Combine(desktop,"preload.cjs"),Path.Combine(bundle,"preload.cjs"),true);
                // Electron needs npm dependencies for the interactive user session; this is not a system service.
                Console.WriteLine("Desktop staged; launch in the interactive user session using Electron. Installer does not claim to generate a desktop executable.");
            }
        }
        if(roles.All(x=>x=="desktop"))return 0;
        var secretsDir=Path.Combine(outDir,"secrets");Directory.CreateDirectory(secretsDir);
        // Only the Auth machine holds the RSA private key. Other nodes must import the SAME
        // verification key from a trusted source; random per-node keys would break every JWT.
        var privateKey=Path.Combine(secretsDir,"private.pem");var publicKey=Path.Combine(secretsDir,"public.pem");
        if(roles.Contains("auth"))
        {
            try {InstallerProfiles.WriteSigningKeys(profile,privateKey,publicKey);}
            catch(Exception e) when(e is InvalidOperationException or CryptographicException){return Error(e.Message);}
            Restrict(privateKey);
        }
        else
        {
            var supplied=Environment.GetEnvironmentVariable("MYSTICEGO_AUTH_PUBLIC_KEY_FILE");
            if(!string.IsNullOrWhiteSpace(supplied))
            {
                if(!File.Exists(supplied))return Error("MYSTICEGO_AUTH_PUBLIC_KEY_FILE does not exist.");
                var incoming=await File.ReadAllTextAsync(supplied);
                try{using var verify=RSA.Create();verify.ImportFromPem(incoming);}
                catch(CryptographicException){return Error("Invalid Auth RSA public key.");}
                if(File.Exists(publicKey) && !CryptographicOperations.FixedTimeEquals(SHA256.HashData(await File.ReadAllBytesAsync(publicKey)),SHA256.HashData(Encoding.UTF8.GetBytes(incoming))))
                    return Error("Auth public key differs from installed key. Explicit key rotation is required.");
                if(!File.Exists(publicKey))await File.WriteAllTextAsync(publicKey,incoming);
            }
            if(!File.Exists(publicKey))return Error("Non-Auth nodes require MYSTICEGO_AUTH_PUBLIC_KEY_FILE or previously provisioned Auth public.pem.");
        }
        Restrict(publicKey);
        var syncKeyPath=Path.Combine(secretsDir,"avatar-sync.key");
        if(!File.Exists(syncKeyPath))await File.WriteAllTextAsync(syncKeyPath,Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
        Restrict(syncKeyPath);
        var syncKey=(await File.ReadAllTextAsync(syncKeyPath)).Trim();
        var manifest=new List<object>();
        foreach(var role in roles.Where(x=>x!="desktop"))
        {
            var service=Services[role];var serviceDir=Path.Combine(outDir,"apps",role);var version=DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")[..8];
            var releaseDir=Path.Combine(serviceDir,"releases",version);
            Directory.CreateDirectory(releaseDir);
            var rid=System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
            Console.WriteLine($"Publishing {role} for {rid}...");
            var status=await Command("dotnet",["publish",Path.Combine(root,service.Project),"-c","Release","-r",rid,"--self-contained","true","-p:PublishSingleFile=true","-o",releaseDir],root);
            if(status!=0)return Error($"Publish failed for {role}; existing service was not replaced. Exit={status}");
            var exe=Path.Combine(releaseDir,service.Executable+(OperatingSystem.IsWindows()?".exe":""));
            if(!File.Exists(exe))return Error($"Publish did not produce executable: {exe}");
            var current=Path.Combine(serviceDir,"current.txt");
            var previous=File.Exists(current)?(await File.ReadAllTextAsync(current)).Trim():null;
            var envPath=Path.Combine(serviceDir,"service.env");Directory.CreateDirectory(serviceDir);
            var environment=new Dictionary<string,string>{
                ["ASPNETCORE_URLS"]=$"http://127.0.0.1:{service.Port}",
                ["Authorization__Jwt__PublicKeyPath"]=publicKey,
                ["Node__DeploymentMode"]=f.Keys.Any(x=>x is "auth-url" or "orchestra-url" or "global-url" or "local-url" or "audit-url")?"remote-distributed":"single-machine",
                ["Node__AuthUrl"]=f.GetValueOrDefault("auth-url")??"http://127.0.0.1:5400",
                ["Node__DataPath"]=Path.Combine(outDir,"data",role+".db"),
                ["Avatar__SyncKey"]=syncKey,
            };
            if(role!="auth")
            {
                var dbFile=Path.Combine(outDir,"data",role+"-postgres.connection");
                if(File.Exists(dbFile))environment["Node__DatabaseConnectionFile"]=dbFile;
            }
            if(role=="auth")
            {
                environment["Authorization__Jwt__PrivateKeyPath"]=privateKey;
                environment["Authorization__ConnectionString"]=Environment.GetEnvironmentVariable("MYSTICEGO_DB_CONNECTION")??("Data Source="+Path.Combine(outDir,"data","auth.db"));
                environment["Authorization__Bootstrap__Login"]=profile=="test"?InstallerProfiles.TestLogin:InstallerProfiles.ProductionLogin;
                environment["Authorization__Bootstrap__Password"]=bootstrapPassword!;
            }
            foreach(var (key,value) in new[]{("orchestra-url","Node__OrchestraUrl"),("global-url","Node__GlobalUrl"),("local-url","Node__LocalUrl"),("audit-url","Node__AuditUrl")})
                if(f.TryGetValue(key,out var url))environment[value]=url;
            if(roles.Contains("orchestra") && (role is "global" or "audit") && !f.ContainsKey("orchestra-url"))
                environment["Node__OrchestraUrl"]="http://127.0.0.1:5500";
            if(role=="local")
            {
                var upstream=f.GetValueOrDefault("global-url")??(roles.Contains("global")?"http://127.0.0.1:5100":
                    f.GetValueOrDefault("audit-url")??(roles.Contains("audit")?"http://127.0.0.1:5350":null));
                if(!string.IsNullOrWhiteSpace(upstream))
                {
                    environment[roles.Contains("global")||f.ContainsKey("global-url")?"Node__GlobalUrl":"Node__AuditUrl"]=upstream;
                    environment["Avatar__UpstreamUrl"]=upstream;
                }
            }
            if(role=="agent")
            {
                var upstream=f.GetValueOrDefault("local-url")??(roles.Contains("local")?"http://127.0.0.1:5200":
                    f.GetValueOrDefault("audit-url")??(roles.Contains("audit")?"http://127.0.0.1:5350":null));
                if(!string.IsNullOrWhiteSpace(upstream))
                {
                    environment[roles.Contains("local")||f.ContainsKey("local-url")?"Node__LocalUrl":"Node__AuditUrl"]=upstream;
                    environment["Avatar__UpstreamUrl"]=upstream;
                }
            }
            if(role=="orchestra")
            {
                environment["WorldAI__StoragePath"]=Path.Combine(outDir,"data","world-ai");
                environment["Orchestra__NativePlansPath"]=Path.Combine(outDir,"data","native-plans");
                environment["Orchestra__NativeJobsPath"]=Path.Combine(outDir,"data","native-jobs");
            }
            if(role is "global" or "local" or "agent" or "audit")
            {
                environment["Avatar__StoragePath"]=Path.Combine(outDir,"data",role,"avatars");
                environment["Avatar__ReplicaPath"]=Path.Combine(outDir,"data",role,"replicas");
            }
            if(role is "orchestra" or "global")
            {
                var idFile=Path.Combine(serviceDir,"node-id.txt");
                if(!File.Exists(idFile)){await File.WriteAllTextAsync(idFile,Guid.NewGuid().ToString());Restrict(idFile);}
                environment["Node__NodeId"]=(await File.ReadAllTextAsync(idFile)).Trim();
            }
            if(role is "local" or "agent" or "audit")
            {
                // Stable credentials per node, not replaced on upgrades.
                var idFile=Path.Combine(serviceDir,"identity.json");
                NodeIdentity id;
                if(File.Exists(idFile))id=JsonSerializer.Deserialize<NodeIdentity>(await File.ReadAllTextAsync(idFile),Json)!;
                else
                {
                    id=new NodeIdentity(Guid.NewGuid().ToString(),Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
                    await File.WriteAllTextAsync(idFile,JsonSerializer.Serialize(id,Json));Restrict(idFile);
                }
                environment["Node__IdentityId"]=id.Id;environment["Node__IdentitySecret"]=id.Secret;
                environment["Node__NodeId"]=id.Id;
            }
            // Services are bound to loopback; external TLS reverse proxy is required before public exposure.
            await File.WriteAllLinesAsync(envPath,environment.Select(kv=>kv.Key+"="+kv.Value.Replace("\r","").Replace("\n","")));
            Restrict(envPath);
            var next=current+".next";await File.WriteAllTextAsync(next,version);File.Move(next,current,true);
            manifest.Add(new{role,version,previous,exe,envPath,port=service.Port});
            if(f.ContainsKey("install-services"))
            {
                int rc=OperatingSystem.IsLinux()?await InstallSystemd(role,exe,envPath):await InstallWindowsService(role,exe,envPath);
                if(rc==0 && !await WaitForHealth(role,service.Port))rc=2;
                if(rc!=0)
                {
                    if(previous!=null)
                    {
                        var priorExe=Path.Combine(serviceDir,"releases",previous,Path.GetFileName(exe));
                        if(File.Exists(priorExe))
                        {
                            Console.Error.WriteLine($"Health check failed for {role}, attempting to restart previous release.");
                            if(OperatingSystem.IsLinux())await InstallSystemd(role,priorExe,envPath);
                            else await InstallWindowsService(role,priorExe,envPath);
                            await File.WriteAllTextAsync(current,previous);
                        }
                    }
                    return Error($"Failed installing/starting {role} (or health check failed); previous version requested where available.");
                }
            }
        }
        await File.WriteAllTextAsync(Path.Combine(outDir,"native-deployment.json"),JsonSerializer.Serialize(manifest,Json));
        Console.WriteLine("Native deployment staged. No Docker used. Services bound to localhost; provide reverse proxy/TLS for external access.");
        if(!f.ContainsKey("install-services"))Console.WriteLine("Pass --install-services as Administrator/root for systemd or Windows Service registration.");
        return 0;
    }
    static async Task<int> OrchestraPlanAsync(string id,Dictionary<string,string> f)
    {
        if(!Regex.IsMatch(id,@"^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))return Error("Invalid Orchestra plan ID");
        if(!f.TryGetValue("orchestra-url",out var server)||!Uri.TryCreate(server,UriKind.Absolute,out var uri)||uri.Scheme!="https")
            return Error("--plan-id requires --orchestra-url https://...");
        var token=Environment.GetEnvironmentVariable("MYSTICEGO_ORCHESTRA_TOKEN");
        if(string.IsNullOrWhiteSpace(token))return Error("Set MYSTICEGO_ORCHESTRA_TOKEN securely in the installer environment.");
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(12)};
        using var req=new HttpRequestMessage(HttpMethod.Get,server.TrimEnd('/')+"/api/v1/orchestra/deployments/native/plans/"+id);
        req.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        try
        {
            using var response=await http.SendAsync(req);
            if(!response.IsSuccessStatusCode)return Error("Orchestra plan fetch rejected: HTTP "+(int)response.StatusCode);
            var bytes=await response.Content.ReadAsByteArrayAsync();
            if(bytes.Length>256_000)return Error("Plan exceeds 256 KB limit");
            var temp=Path.Combine(Path.GetTempPath(),"mysticego-orchestra-plan-"+Guid.NewGuid().ToString("N")+".json");
            try
            {
                await File.WriteAllBytesAsync(temp,bytes);
                return await ClusterAsync(temp,f);
            }
            finally{File.Delete(temp);}
        }
        catch(HttpRequestException e){return Error("Orchestra connection failed: "+e.GetType().Name);}
        catch(TaskCanceledException){return Error("Orchestra timed out");}
    }
    static async Task<int> ClusterAsync(string path,Dictionary<string,string> flags)
    {
        NativePlan? plan;
        try {plan=JsonSerializer.Deserialize<NativePlan>(await File.ReadAllTextAsync(path),Json);}
        catch(Exception e){return Error("Invalid manifest: "+e.Message);}
        if(plan?.Nodes is not {Length:>0})return Error("Manifest needs nodes.");
        if(plan.Nodes.Any(n=>n.Host!="local"&&!SshTarget.IsMatch(n.Host))||plan.Nodes.GroupBy(n=>n.Host,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))
            return Error("Unsafe or duplicate hosts.");
        foreach(var n in plan.Nodes)
        {
            var roles=n.Roles.Select(x=>x.ToLowerInvariant()).ToArray();
            if(roles.Length==0||roles.Any(x=>!Supported.Contains(x)||x=="desktop"))return Error("Remote native manifest supports server API roles only; Desktop is installed on the local interactive workstation.");
            try{CheckDependencies(roles,new Dictionary<string,string>{["auth-url"]=n.AuthUrl??"",["orchestra-url"]=n.OrchestraUrl??"",["global-url"]=n.GlobalUrl??"",["local-url"]=n.LocalUrl??"",["audit-url"]=n.AuditUrl??""}.Where(x=>x.Value.Length>0).ToDictionary(x=>x.Key,x=>x.Value));}
            catch(ArgumentException e){return Error(n.Host+": "+e.Message);}
        }
        if(flags.ContainsKey("validate-only")||flags.ContainsKey("dry-run"))
        {foreach(var n in plan.Nodes)Console.WriteLine($"VALID native {n.Host}: {string.Join(',',n.Roles)}");return 0;}
        // Test keys are never deployed via SSH or Orchestra manifests.
        try {InstallerProfiles.ValidateTestScope(InstallerProfiles.Get(flags),flags,remote:true);}
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){return Error(e.Message);}
        // Remote execution is intentionally limited to pre-enrolled administrator-controlled SSH hosts.
        // Bootstrap secrets are never passed in the manifest or over command-line flags.
        if(plan.Nodes.Any(n=>n.Host!="local" && n.Roles.Contains("auth",StringComparer.OrdinalIgnoreCase)))
            return Error("Native multi-host bootstrap of Auth requires a secure secret distribution mechanism; deploy Auth locally first. Remote password injection is intentionally unsupported.");
        string profile;
        try { profile=InstallerProfiles.Get(flags); InstallerProfiles.ValidateTestScope(profile,flags,remote:true); }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException){return Error(e.Message);}
        var root=Path.GetFullPath(flags.GetValueOrDefault("root")??FindRoot());
        var suppliedPublic=Environment.GetEnvironmentVariable("MYSTICEGO_AUTH_PUBLIC_KEY_FILE");
        var publicPem=!string.IsNullOrWhiteSpace(suppliedPublic)&&File.Exists(suppliedPublic)?suppliedPublic:
            Path.Combine(root,"deployment-native","secrets","public.pem");
        if(plan.Nodes.Any(n=>n.Host!="local") && !File.Exists(publicPem))
            return Error("Remote native deployment needs trusted Auth public.pem on Orchestra machine; set MYSTICEGO_AUTH_PUBLIC_KEY_FILE.");
        foreach(var n in plan.Nodes.Where(x=>x.Host!="local"))
        {
            if(!await CheckRemote(n.Host,"command -v dotnet >/dev/null && command -v unzip >/dev/null"))return Error("Remote native preflight failed (dotnet/unzip needed on SSH host): "+n.Host);
        }
        if(flags.ContainsKey("preflight-only"))return 0;
        var archive=Path.Combine(Path.GetTempPath(),"mysticego-native-"+Guid.NewGuid().ToString("N")+".zip");
        try
        {
            SourceBundle.Pack(root,archive);
            foreach(var n in plan.Nodes.OrderBy(n=>n.Roles.Min(RoleRank)))
            {
                var roleCsv=string.Join(',',n.Roles.OrderBy(RoleRank));
                if(n.Host=="local")
                {
                    var local=new Dictionary<string,string>(flags,StringComparer.OrdinalIgnoreCase){["roles"]=roleCsv};
                    local.Remove("manifest");local.Remove("plan-id");
                    PutEndpoints(local,n);
                    var status=await RunAsync(local);if(status!=0)return status;
                }
                else
                {
                    // Use a dedicated per-deployment directory, not a path specified by remote user input.
                    const string dest="~/mysticego-native-src";
                    if(await Command("ssh",["-o","BatchMode=yes",n.Host,"mkdir -p "+dest],root)!=0)return 2;
                    if(await Command("scp",[archive,n.Host+":/tmp/mysticego-native-src.zip"],root)!=0)return 2;
                    var remoteKey="/tmp/mysticego-auth-public-"+Guid.NewGuid().ToString("N")+".pem";
                    if(await Command("scp",[publicPem,n.Host+":"+remoteKey],root)!=0)return 2;
                    var args=new List<string>{"--mode native","--roles "+roleCsv};
                    foreach(var (key,val) in Endpoints(n))if(!string.IsNullOrEmpty(val))args.Add("--"+key+" "+Shell(val));
                    if(flags.ContainsKey("install-services"))args.Add("--install-services");
                    // SSH runs ONLY on preapproved host; --install-services needs sudo permissions set on host.
                    var remote="unzip -q -o /tmp/mysticego-native-src.zip -d "+dest+" && rm -f /tmp/mysticego-native-src.zip && cd "+dest+
                        " && MYSTICEGO_AUTH_PUBLIC_KEY_FILE="+Shell(remoteKey)+" dotnet run --project tools/MysticEgo.Installer -- "+string.Join(" ",args)+
                        "; result=$?; rm -f "+Shell(remoteKey)+"; exit $result";
                    if(await Command("ssh",["-o","BatchMode=yes",n.Host,remote],root)!=0)return 2;
                }
            }
        }
        finally{if(File.Exists(archive))File.Delete(archive);}
        return 0;
    }
    static int RoleRank(string role)=>role.ToLowerInvariant() switch
    {
        "auth"=>0,"orchestra"=>1,"global"=>2,"audit"=>2,"local"=>3,"agent"=>4,"desktop"=>5,_=>99
    };
    static void CheckDependencies(string[] roles,IDictionary<string,string> f)
    {
        bool Present(string role,string key)=>roles.Contains(role)||(f.TryGetValue(key,out var v)&&!string.IsNullOrWhiteSpace(v));
        if(roles.Any(x=>x!="desktop")&&!Present("auth","auth-url"))throw new ArgumentException("Native services require Auth locally or --auth-url.");
        if(roles.Contains("global")&&!Present("orchestra","orchestra-url"))throw new ArgumentException("Global requires Orchestra.");
        if(roles.Contains("local")&&!Present("global","global-url")&&!Present("audit","audit-url"))throw new ArgumentException("Local requires Global or Audit.");
        if(roles.Contains("agent")&&!Present("local","local-url")&&!Present("audit","audit-url"))throw new ArgumentException("Agent requires Local or Audit.");
        foreach(var key in new[]{"auth-url","orchestra-url","global-url","local-url","audit-url"})
            if(f.TryGetValue(key,out var u)&&(!Uri.TryCreate(u,UriKind.Absolute,out var parsed)||parsed.Scheme!="https"||parsed.UserInfo.Length!=0||parsed.Fragment.Length!=0))
                throw new ArgumentException(key+" must be an HTTPS URL without credentials or fragment.");
    }
    static async Task<bool> WaitForHealth(string role,int port)
    {
        var path=role=="auth"?"/health":"/health/live";
        using var http=new HttpClient{Timeout=TimeSpan.FromSeconds(2)};
        for(int attempt=0;attempt<15;attempt++)
        {
            try{using var resp=await http.GetAsync($"http://127.0.0.1:{port}{path}");if(resp.IsSuccessStatusCode)return true;}
            catch(HttpRequestException){}catch(TaskCanceledException){}
            await Task.Delay(1000);
        }
        return false;
    }
    static async Task<int> InstallSystemd(string role,string exe,string env)
    {
        var unit="mysticego-"+role+".service";
        var unitPath=Path.Combine("/etc/systemd/system",unit);
        // EnvironmentFile values generated above; newline is stripped; use machine-owner-only permissions.
        var content="[Unit]\nDescription=MysticEgo "+role+" (native)\nAfter=network-online.target\nWants=network-online.target\n\n[Service]\nType=simple\nWorkingDirectory="+Path.GetDirectoryName(exe)+"\nEnvironmentFile="+env+"\nExecStart="+exe+"\nRestart=on-failure\nRestartSec=5\nNoNewPrivileges=true\nProtectSystem=strict\nReadWritePaths="+Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(env)!)!) +"\n\n[Install]\nWantedBy=multi-user.target\n";
        await File.WriteAllTextAsync(unitPath,content);
        int reload=await Command("systemctl",["daemon-reload"],"/");
        if(reload!=0)return reload;
        var enable=await Command("systemctl",["enable",unit],"/");
        return enable==0?await Command("systemctl",["restart",unit],"/"):enable;
    }
    static async Task<int> InstallWindowsService(string role,string exe,string env)
    {
        // A Windows Service does NOT automatically load .env; use dedicated Windows service wrapper mode.
        // The same installer executable runs as the service wrapper and starts the published API with configured environment.
        var installer=Environment.ProcessPath;
        if(string.IsNullOrEmpty(installer)||!File.Exists(installer)||Path.GetFileNameWithoutExtension(installer).Equals("dotnet",StringComparison.OrdinalIgnoreCase))
            return Error("For Windows Service registration publish the installer as a self-contained executable.");
        // Service control manager must not depend on a temporary installer download path.
        var stableDir=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(env)!)!,"installer");
        Directory.CreateDirectory(stableDir);
        var hash=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installer)))[..16];
        var stableInstaller=Path.Combine(stableDir,"MysticEgo.Installer."+hash+".exe");
        if(!File.Exists(stableInstaller))File.Copy(installer,stableInstaller);
        installer=stableInstaller;
        var name="MysticEgo."+role;
        var bin="\""+installer+"\" --service-host --service-name \""+name+"\" --service-exe \""+exe+"\" --service-env \""+env+"\"";
        // Existing service: update binary configuration; absent: create service.
        var qc=await Command("sc.exe",["query",name],Environment.CurrentDirectory);
        if(qc==0)await Command("sc.exe",["stop",name],Environment.CurrentDirectory);
        var arguments=qc==0?new[]{"config",name,"binPath=",bin,"start=","auto"}:new[]{"create",name,"binPath=",bin,"start=","auto"};
        int rc=await Command("sc.exe",arguments,Environment.CurrentDirectory);
        if(rc!=0)return rc;
        return await Command("sc.exe",["start",name],Environment.CurrentDirectory);
    }
    internal static async Task<int> ServiceHostAsync(Dictionary<string,string> f)
    {
        if(!f.TryGetValue("service-exe",out var exe)||!f.TryGetValue("service-env",out var env)||!File.Exists(exe)||!File.Exists(env))return Error("Invalid service wrapper arguments.");
        // The wrapper itself is a true Windows Service (SCM compatible), not a console process.
        await Host.CreateDefaultBuilder()
            .UseWindowsService(options=>options.ServiceName=f.GetValueOrDefault("service-name")??"MysticEgo.Native")
            .ConfigureServices(services=>services.AddHostedService(_=>new NativeChildService(exe,env)))
            .Build().RunAsync();
        return 0;
    }
    sealed class NativeChildService(string exe,string env) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            using var process=new Process{StartInfo=new ProcessStartInfo(exe){WorkingDirectory=Path.GetDirectoryName(exe)!,UseShellExecute=false}};
            foreach(var line in await File.ReadAllLinesAsync(env,stoppingToken))
            {var p=line.IndexOf('=');if(p>0)process.StartInfo.Environment[line[..p]]=line[(p+1)..];}
            if(!process.Start())throw new InvalidOperationException("Failed to launch native API");
            try{await process.WaitForExitAsync(stoppingToken);}
            catch(OperationCanceledException){if(!process.HasExited)process.Kill(entireProcessTree:true);}
        }
    }
    static bool IsAdmin() => OperatingSystem.IsWindows() ? new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator) : Environment.UserName=="root";
    static void Restrict(string path){if(!OperatingSystem.IsWindows())File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);}
    static async Task<bool> CheckToolAsync(string cmd,string arg)=>await Command(cmd,[arg],Environment.CurrentDirectory)==0;
    static async Task<bool> HasDotNet10Async()
    {
        try
        {
            var p=new ProcessStartInfo("dotnet"){UseShellExecute=false,RedirectStandardOutput=true};
            p.ArgumentList.Add("--list-sdks");using var process=Process.Start(p);
            if(process==null)return false;
            var output=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
            return process.ExitCode==0 && output.Split('\n').Any(line=>line.TrimStart().StartsWith("10.",StringComparison.Ordinal));
        }
        catch(Exception){return false;}
    }
    static async Task<bool> VerifyNativeUpstreams(IReadOnlyDictionary<string,string> flags)
    {
        var services=new[]{("auth-url","Auth","/health"),("orchestra-url","Orchestra","/health/live"),
            ("global-url","Global","/health/live"),("local-url","Local","/health/live"),("audit-url","AuditOrchestra","/health/live")};
        using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false};
        using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(7)};
        foreach(var (key,kind,path) in services)
        {
            if(!flags.TryGetValue(key,out var uri))continue;
            try
            {
                using var result=await http.GetAsync(uri.TrimEnd('/')+path);
                if(!result.IsSuccessStatusCode){Console.Error.WriteLine(key+": unhealthy upstream");return false;}
                if(kind=="Auth")continue;
                using var response=JsonDocument.Parse(await result.Content.ReadAsStringAsync());
                if(!response.RootElement.TryGetProperty("kind",out var actual)||!string.Equals(actual.GetString(),kind,StringComparison.OrdinalIgnoreCase))
                {Console.Error.WriteLine(key+": unexpected service kind");return false;}
            }
            catch(Exception e) when(e is HttpRequestException or JsonException or TaskCanceledException or UriFormatException)
            {Console.Error.WriteLine(key+": health endpoint unavailable");return false;}
        }
        return true;
    }
    static void CopyTree(string from,string to)
    {
        Directory.CreateDirectory(to);
        foreach(var file in Directory.EnumerateFiles(from))File.Copy(file,Path.Combine(to,Path.GetFileName(file)),true);
        foreach(var dir in Directory.EnumerateDirectories(from))CopyTree(dir,Path.Combine(to,Path.GetFileName(dir)));
    }
    static async Task<bool> CheckRemote(string host,string command)=>await Command("ssh",["-o","BatchMode=yes","-o","ConnectTimeout=8",host,command],Environment.CurrentDirectory)==0;
    static async Task<int> Command(string cmd,IEnumerable<string> args,string cwd)
    {
        try{var exe=OperatingSystem.IsWindows()&&cmd=="npm"?"cmd.exe":cmd;
            var p=new ProcessStartInfo(exe){WorkingDirectory=cwd,UseShellExecute=false};
            if(exe=="cmd.exe"){p.ArgumentList.Add("/d");p.ArgumentList.Add("/c");p.ArgumentList.Add("npm");}
            foreach(var a in args)p.ArgumentList.Add(a);
            using var proc=Process.Start(p);if(proc==null)return 2;await proc.WaitForExitAsync();return proc.ExitCode;}
        catch(Exception e){Console.Error.WriteLine(cmd+": "+e.Message);return 2;}
    }
    static string FindRoot(){var d=new DirectoryInfo(Environment.CurrentDirectory);while(d!=null){if(File.Exists(Path.Combine(d.FullName,"MysticEgo.Distributed.slnx")))return d.FullName;d=d.Parent;}return Environment.CurrentDirectory;}
    static string Shell(string s)=>"'"+s.Replace("'","'\\''")+"'";
    static void PutEndpoints(Dictionary<string,string> f,NativeTarget n){foreach(var (key,val) in Endpoints(n))if(!string.IsNullOrWhiteSpace(val))f[key]=val;}
    static IEnumerable<(string Key,string? Value)> Endpoints(NativeTarget n)=>[("auth-url",n.AuthUrl),("orchestra-url",n.OrchestraUrl),("global-url",n.GlobalUrl),("local-url",n.LocalUrl),("audit-url",n.AuditUrl)];
    static int Error(string text){Console.Error.WriteLine("Native deployment: "+text);return 2;}
    sealed record NodeIdentity(string Id,string Secret);
    sealed record NativePlan(NativeTarget[] Nodes);
    sealed record NativeTarget(string Host,string[] Roles,string? AuthUrl=null,string? OrchestraUrl=null,string? GlobalUrl=null,string? LocalUrl=null,string? AuditUrl=null);
}
