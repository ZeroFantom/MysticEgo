using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// One C# installer for Windows/Linux; no Python, PowerShell installer or shell installers.
// Publish platform-specific self-contained binaries from this ONE project.
// One executable has both container and native modes. Native never invokes Docker.
Console.InputEncoding = System.Text.Encoding.UTF8;
Console.OutputEncoding = new System.Text.UTF8Encoding(false);
var earlyFlags=Parse(args);
if (earlyFlags.ContainsKey("host-fingerprint"))
{
    Console.WriteLine(MysticEgo.Deployment.HostFingerprint.Read());
    return;
}
if(earlyFlags.ContainsKey("lab") || earlyFlags.ContainsKey("join") || earlyFlags.ContainsKey("pack-lab") || earlyFlags.ContainsKey("lm-setup") || earlyFlags.ContainsKey("desktop") || earlyFlags.ContainsKey("verify"))
{
    try
    {
        if (earlyFlags.ContainsKey("pack-lab")) await LabPackage.Build(FindRoot(),earlyFlags.ContainsKey("reuse-web"));
        else if(earlyFlags.ContainsKey("desktop")) await LabExtras.Desktop(FindRoot());
        else if(earlyFlags.ContainsKey("verify")) await LabExtras.Verify(earlyFlags);
        else if (earlyFlags.ContainsKey("lm-setup")) await LabModel.Setup(CancellationToken.None, earlyFlags.GetValueOrDefault("model-role") ?? "agent");
        else await new LabHost(earlyFlags).Run();
    }
    catch(OperationCanceledException) { }
    catch(Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode=2; }
    return;
}
if(earlyFlags.ContainsKey("service-host"))
{
 Environment.ExitCode=await NativeDeployment.ServiceHostAsync(earlyFlags);return;
}
if(string.Equals(earlyFlags.GetValueOrDefault("mode"),"native",StringComparison.OrdinalIgnoreCase))
{
 Environment.ExitCode=await NativeDeployment.RunAsync(earlyFlags);return;
}
var supported=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"auth","orchestra","global","local","agent","audit","desktop","vpn"};
var flags=Parse(args);
string profile;
try{profile=InstallerProfiles.Get(flags);InstallerProfiles.ValidateTestScope(profile,flags);}
catch(Exception e) when(e is ArgumentException or InvalidOperationException){Console.Error.WriteLine(e.Message);Environment.ExitCode=2;return;}
var allowedFlags=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"help","h","manifest","root","roles","output","dry-run","preflight-only","validate-only","config-only","no-build","start-desktop","vision-model","auth-url","orchestra-url","global-url","local-url","audit-url","mode","install-services","plan-id","profile","no-auto-deps"};
var unknownFlags=flags.Keys.Where(x=>!allowedFlags.Contains(x)).ToArray();
if(unknownFlags.Length>0){Console.Error.WriteLine("Unknown installer flags: "+string.Join(", ",unknownFlags));Environment.ExitCode=2;return;}

if(flags.ContainsKey("help")||flags.ContainsKey("h")){Usage();return;}
if(flags.TryGetValue("manifest",out var manifestPath))
{
 var plan=JsonSerializer.Deserialize<DeploymentPlan>(await File.ReadAllTextAsync(manifestPath),new JsonSerializerOptions(JsonSerializerDefaults.Web));
 if(plan?.Nodes is not {Length:>0})throw new ArgumentException("Deployment manifest must contain nodes");
 if(plan.Nodes.GroupBy(x=>x.Host,StringComparer.OrdinalIgnoreCase).Any(g=>g.Count()>1))throw new ArgumentException("Duplicate deployment targets are not permitted");
 var source=Path.GetFullPath(flags.GetValueOrDefault("root")??FindRoot());
 // Validate the whole plan BEFORE executing any deployment.
 foreach(var check in plan.Nodes)
 {
  var declared=(check.Roles??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);
  if(declared.Count==0 || declared.Any(x=>!supported.Contains(x)))throw new ArgumentException("Unknown or empty role in manifest");
  CheckNodeLinks(check,declared);
  // Verify the upstream endpoint and deployment toolchain BEFORE any installation begins.
  // For a multi-host manifest, upstreams that will be created by this same plan are not probed here.
  if(check.Host!="local" && !Regex.IsMatch(check.Host??"",@"^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$"))
   throw new ArgumentException("Unsafe SSH target");
 }
 // Manifest validation is intentionally offline: validate syntax, topology and dependency links without touching hosts.
 if(flags.ContainsKey("validate-only") || flags.ContainsKey("dry-run"))
 {
  foreach(var node in plan.Nodes)Console.WriteLine($"VALID {node.Host}: {string.Join(",",node.Roles??[])}");
  Console.WriteLine("Manifest validation completed without network or filesystem changes.");return;
 }
 // Stage 2: preflight tools on every target before making deployment changes.
 // Tool existence checks only; no archive copy, no directory creation, no service restarts.
 foreach(var node in plan.Nodes)
 {
  if(node.Host=="local")continue;
  if(!await Run("ssh",new[]{"-o","BatchMode=yes","-o","ConnectTimeout=8",node.Host,
       "command -v dotnet >/dev/null && command -v docker >/dev/null && docker compose version >/dev/null && command -v unzip >/dev/null"},source))
  {Console.Error.WriteLine("Preflight rejected remote target: "+node.Host);Environment.ExitCode=2;return;}
 }
 if(flags.ContainsKey("preflight-only") || flags.ContainsKey("dry-run")){
  foreach(var node in plan.Nodes)Console.WriteLine($"PLAN {node.Host}: {string.Join(",",node.Roles??[])}");
  Console.WriteLine("Manifest preflight completed; no deployment attempted.");return;
 }
 foreach(var node in plan.Nodes)
 {
  var group=(node.Roles??[]).Select(x=>x.ToLowerInvariant()).Distinct().ToArray();
  if(group.Length==0||group.Any(x=>!supported.Contains(x)))throw new ArgumentException("Unknown role in manifest");
  var csv=string.Join(",",group);
  var optional=new List<string>();
  void Push(string flag,string? url){if(!string.IsNullOrWhiteSpace(url)){optional.Add("--"+flag);optional.Add(url);}}
  Push("auth-url",node.AuthUrl);Push("orchestra-url",node.OrchestraUrl);Push("global-url",node.GlobalUrl);
  Push("local-url",node.LocalUrl);Push("audit-url",node.AuditUrl);
  foreach(var flag in new[]{"config-only","no-build"})if(flags.ContainsKey(flag))optional.Add("--"+flag);
  if(node.Host=="local")
  {
   if(!await Run("dotnet",new[]{"run","--project",Path.Combine(source,"tools/MysticEgo.Installer/MysticEgo.Installer.csproj"),"--","--root",source,"--roles",csv}.Concat(optional),source))return;
  }
  else
  {
   // SSH targets: restricted shell-safe hostnames, Linux server with SSH/.NET SDK/Docker/npm.
   if(!Regex.IsMatch(node.Host??"",@"^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$"))throw new ArgumentException("Unsafe SSH target");
   if(!await Run("ssh",[node.Host,"mkdir -p ~/mysticego"],source))return;
   var zipPath=Path.Combine(Path.GetTempPath(),"mysticego-src-"+Guid.NewGuid().ToString("N")+".zip");
   try{SourceBundle.Pack(source,zipPath);if(!await Run("scp",[zipPath,node.Host+":/tmp/mysticego-source.zip"],source))return;}
   finally{File.Delete(zipPath);}
   if(!await Run("ssh",[node.Host,"unzip -q -o /tmp/mysticego-source.zip -d ~/mysticego && rm /tmp/mysticego-source.zip && cd ~/mysticego && dotnet run --project tools/MysticEgo.Installer/MysticEgo.Installer.csproj -- --roles "+csv+" "+string.Join(" ",optional.Select(x=>"'"+x.Replace("'","")+"'"))],source))return;
  }
 }
 return;
}
if(!flags.TryGetValue("roles",out var raw))
{
 Console.WriteLine("MysticEgo Unified Installer 1.0.0 beta.6\nРоли: auth,orchestra,global,local,agent,audit,desktop,vpn");
 Console.Write("Компоненты через запятую [auth,orchestra,global,local,agent,audit]: ");
 raw=Console.ReadLine();if(string.IsNullOrWhiteSpace(raw))raw="auth,orchestra,global,local,agent,audit";
}
var roles=raw.Split(',',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).Select(x=>x.ToLowerInvariant()).Distinct().ToArray();
if(roles.Length==0||roles.Any(x=>!supported.Contains(x))){Console.Error.WriteLine("Неизвестные компоненты.");Environment.ExitCode=2;return;}
try { ValidateDependencies(roles.ToHashSet(StringComparer.OrdinalIgnoreCase),flags);
 if(!roles.Contains("auth") && roles.Any(x=>x is not ("desktop" or "vpn")) && !flags.ContainsKey("auth-url"))
    throw new ArgumentException("Selected APIs require Auth (select auth or specify --auth-url HTTPS).");
 }
catch(ArgumentException e){Console.Error.WriteLine("Dependency preflight: "+e.Message);Environment.ExitCode=2;return;}
if(flags.ContainsKey("validate-only")) {Console.WriteLine("Role dependency graph is valid; no external probes or files written.");return;}
var root=Path.GetFullPath(flags.GetValueOrDefault("root")??FindRoot());
if(!File.Exists(Path.Combine(root,"MysticEgo.Distributed.slnx"))){Console.Error.WriteLine("Root must contain MysticEgo.Distributed.slnx");Environment.ExitCode=2;return;}
var output=Path.GetFullPath(flags.GetValueOrDefault("output")??Path.Combine(root,profile=="test"?"deployment-test":"deployment"));
try{InstallerProfiles.ValidateTarget(output,profile);}
catch(InvalidOperationException e){Console.Error.WriteLine(e.Message);Environment.ExitCode=2;return;}
var dryRun=flags.ContainsKey("dry-run");
if(!dryRun && !await VerifyExternalUpstreams(roles,flags))
{Console.Error.WriteLine("External dependency check failed. Deployment aborted before writing files.");Environment.ExitCode=2;return;}
if(!dryRun && !await VerifyPrerequisites(roles,flags))
{Console.Error.WriteLine("Toolchain preflight failed. No deployment files were created.");Environment.ExitCode=2;return;}
if(flags.ContainsKey("preflight-only")){Console.WriteLine("Preflight passed; no files were written.");return;}
// Resolve bootstrap before modifying the target; no insecure default is used in production.
string? bootstrapPassword=null;
if(roles.Contains("auth") && !dryRun)
{
 var existingEnv=Path.Combine(output,".env");
 string? oldPassword=null;
 if(File.Exists(existingEnv))oldPassword=File.ReadAllLines(existingEnv)
     .FirstOrDefault(line=>line.StartsWith("MYSTIC_ADMIN_PASSWORD=",StringComparison.Ordinal))?.Split('=',2)[1];
 try{bootstrapPassword=InstallerProfiles.ResolveBootstrapPassword(profile,oldPassword);}
 catch(Exception e) when(e is ArgumentException or InvalidOperationException){Console.Error.WriteLine(e.Message);Environment.ExitCode=2;return;}
}
if(!dryRun){Directory.CreateDirectory(output);InstallerProfiles.Mark(output,profile);}
Console.WriteLine($"Root: {root}\nRoles: {string.Join(", ",roles)}\nOutput: {output}");
if(roles.Contains("desktop"))
{
 Console.WriteLine("Desktop overlay: Electron + native C# geometry helper (Windows; Linux X11/wmctrl).");
 if(!dryRun)
 {
  if(!await Run("npm",["install"],Path.Combine(root,"desktop")))return;
  var desktopWasm=Path.Combine(root,"desktop","node_modules","@mediapipe","tasks-vision","wasm");
  var desktopVision=Path.Combine(root,"desktop","renderer","public","vision");
  if(Directory.Exists(desktopWasm))CopyDirectory(desktopWasm,Path.Combine(desktopVision,"wasm"));
  if(flags.TryGetValue("vision-model",out var modelForDesktop)){
    if(!File.Exists(modelForDesktop)){Console.Error.WriteLine("Face landmark model is missing: "+modelForDesktop);Environment.ExitCode=2;return;}
    Directory.CreateDirectory(desktopVision);File.Copy(modelForDesktop,Path.Combine(desktopVision,"face_landmarker.task"),true);
  }
  if(!await Run("npm",["run","build"],Path.Combine(root,"desktop")))return;
  var native=Path.Combine(root,"desktop","native");
  if(!await Run("dotnet",["publish",Path.Combine(native,"MysticEgo.DesktopGeometry.csproj"),"-c","Release","-r",System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier,"--self-contained","true","-o",Path.Combine(native,"published")],root))return;
  if(flags.ContainsKey("start-desktop"))if(!await Run("npm",["run","desktop"],Path.Combine(root,"desktop")))return;
 }
}
var serviceRoles=roles.Where(r=>r is not ("desktop" or "vpn")).ToArray();
if(serviceRoles.Length>0 || roles.Contains("vpn"))
{
 var runtime=Path.Combine(output,"secrets");if(!dryRun)Directory.CreateDirectory(runtime);
 var envFile=Path.Combine(output,".env");var secrets=new Dictionary<string,string>();
 if(File.Exists(envFile))
 {
  foreach(var line in File.ReadAllLines(envFile)) {var split=line.IndexOf('=');if(split>0)secrets[line[..split]]=line[(split+1)..];}
 }
 string Secret(string name){if(!secrets.TryGetValue(name,out var value)||string.IsNullOrEmpty(value))secrets[name]=value=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));return value;}
 Secret("MYSTIC_DB_PASSWORD");Secret("MYSTIC_AVATAR_SYNC_KEY");
 if(roles.Contains("auth"))
 {
   secrets["MYSTIC_ADMIN_PASSWORD"]=bootstrapPassword!;
 }
 else if(!secrets.ContainsKey("MYSTIC_ADMIN_PASSWORD"))secrets["MYSTIC_ADMIN_PASSWORD"]=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
 if(!secrets.ContainsKey("MYSTIC_VPN_LINK_KEY"))secrets["MYSTIC_VPN_LINK_KEY"]=Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
 foreach(var role in new[]{"local","agent","audit"}){Secret(role.ToUpperInvariant()+"_IDENTITY_SECRET");if(!secrets.ContainsKey(role.ToUpperInvariant()+"_IDENTITY_ID"))secrets[role.ToUpperInvariant()+"_IDENTITY_ID"]=Guid.NewGuid().ToString();}
 if(!dryRun)
 {
  var priv=Path.Combine(runtime,"private.pem");var pub=Path.Combine(runtime,"public.pem");
  if(!File.Exists(priv)||!File.Exists(pub))
  {
   using var rsa=RSA.Create(3072);
   await File.WriteAllTextAsync(priv,rsa.ExportPkcs8PrivateKeyPem());await File.WriteAllTextAsync(pub,rsa.ExportSubjectPublicKeyInfoPem());
  }
  if(!OperatingSystem.IsWindows())
  {
   File.SetUnixFileMode(priv,UnixFileMode.UserRead|UnixFileMode.UserWrite);
   File.SetUnixFileMode(pub,UnixFileMode.UserRead|UnixFileMode.UserWrite);
  }
  await File.WriteAllLinesAsync(envFile,secrets.Select(kv=>kv.Key+"="+kv.Value));
  if(!OperatingSystem.IsWindows())File.SetUnixFileMode(envFile,UnixFileMode.UserRead|UnixFileMode.UserWrite);
 }
 var compose=Compose(roles.Where(x=>x!="desktop").ToArray(),root,output,flags);
 var composeFile=Path.Combine(output,"compose.yaml");
 if(!dryRun)await File.WriteAllTextAsync(composeFile,compose);
 else Console.WriteLine(compose);
 if(!dryRun && !flags.ContainsKey("no-build"))
 {
  Console.WriteLine("Building frontend workspaces...");
  if(!await Run("npm",["install"],Path.Combine(root,"frontend")))return;
  // Offline/local MediaPipe WASM files from installed package; never load camera runtime from CDN.
  var wasmSource=Path.Combine(root,"frontend","node_modules","@mediapipe","tasks-vision","wasm");
  foreach(var webRole in new[]{"audit","agent"})
  {
   var publicVision=Path.Combine(root,"frontend","apps",webRole,"public","vision");
   if(Directory.Exists(wasmSource))CopyDirectory(wasmSource,Path.Combine(publicVision,"wasm"));
   if(flags.TryGetValue("vision-model",out var visionModel))
   {
    if(!File.Exists(visionModel)){Console.Error.WriteLine("Face landmark model is missing: "+visionModel);Environment.ExitCode=2;return;}
    Directory.CreateDirectory(publicVision);File.Copy(visionModel,Path.Combine(publicVision,"face_landmarker.task"),true);
   }
  }
  if(!await Run("npm",["run","build"],Path.Combine(root,"frontend")))return;
  foreach(var role in serviceRoles.Where(x=>x!="auth"))
  {
   var name=role switch {"audit"=>"AuditOrchestra",_=>char.ToUpperInvariant(role[0])+role[1..]};
   var src=Path.Combine(root,"frontend","apps",role,"dist");
   var dest=Path.Combine(root,"services","Api."+name,"wwwroot");
   if(Directory.Exists(src)){CopyDirectory(src,dest);Console.WriteLine($"Web UI bundled: {role}");}
  }
 }
 if(!dryRun)
 {
  Console.WriteLine("Configuration generated. Keep deployment/.env and deployment/secrets private.");
  if(!flags.ContainsKey("config-only"))
  {
   if(!await Run("docker",["compose","--env-file",envFile,"-f",composeFile,"up","-d","--build"],root))return;
   Console.WriteLine("Services started. Verify health and provision node identities using Auth before production.");
  }
 }
}
Console.WriteLine("Installer finished.");

static Dictionary<string,string> Parse(string[] args)
{
 var d=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
 for(int i=0;i<args.Length;i++)if(args[i].StartsWith("--")){var name=args[i][2..];d[name]=(i+1<args.Length&&!args[i+1].StartsWith("--"))?args[++i]:"true";}
 return d;
}
static void Usage()=>Console.WriteLine("MysticEgo.Installer [--profile test|production] [--mode native|docker] --manifest hosts.json OR --roles auth,global,local,agent,audit,orchestra,desktop,vpn [--root PATH] [--output PATH] [--validate-only] [--dry-run] [--preflight-only] [--config-only] [--no-build] [--start-desktop]\nIn native mode the published self-contained installer can auto-install verified .NET SDK 10 and Node.js/npm into a user toolchain cache. --no-auto-deps disables downloads. Docker mode requires preinstalled Docker.");
static string FindRoot()
{
 var directory=new DirectoryInfo(Environment.CurrentDirectory);
 while(directory!=null){if(File.Exists(Path.Combine(directory.FullName,"MysticEgo.Distributed.slnx")))return directory.FullName;directory=directory.Parent;}
 return Environment.CurrentDirectory;
}
static async Task<bool> Run(string command,IEnumerable<string> arguments,string cwd)
{
 var invoke=OperatingSystem.IsWindows() && command=="npm" ? "cmd.exe" : command;
 var p=new ProcessStartInfo(invoke){WorkingDirectory=cwd,UseShellExecute=false};
 if(invoke=="cmd.exe"){p.ArgumentList.Add("/d");p.ArgumentList.Add("/c");p.ArgumentList.Add("npm");}
 foreach(var arg in arguments)p.ArgumentList.Add(arg);
 try {using var proc=Process.Start(p);if(proc is null)throw new Exception("Failed to start process");await proc.WaitForExitAsync();if(proc.ExitCode==0)return true;
    Console.Error.WriteLine($"{command} exited {proc.ExitCode}");Environment.ExitCode=proc.ExitCode;return false;}
 catch(Exception e){Console.Error.WriteLine($"Missing dependency or error: {command}: {e.Message}");Environment.ExitCode=1;return false;}
}
// Tool discovery is intentionally read-only, and it executes before deployment/ is created.
static async Task<bool> VerifyPrerequisites(string[] roles,IReadOnlyDictionary<string,string> flags)
{
 var sdk=roles.Contains("desktop");
 var node=(!flags.ContainsKey("no-build") && roles.Any(r=>r is not ("vpn" or "desktop"))) || roles.Contains("desktop");
 if(!await InstallerDependencies.EnsureAsync(sdk,node,
         installMissing:!flags.ContainsKey("preflight-only") && !flags.ContainsKey("no-auto-deps")))
     return false;
 // Docker is intentionally not installed automatically: it requires OS-specific daemon,
 // virtualization, administrative configuration and sometimes a restart. Use --mode native.
 if(roles.Any(r=>r!="desktop"))
 {
     if(!await ToolRuns("docker",["--version"]) || !await ToolRuns("docker",["compose","version"]))
     {
         Console.Error.WriteLine("Docker Compose is unavailable. For a Docker-free installation use --mode native.");
         return false;
     }
 }
 return true;
}

static async Task<bool> ToolRuns(string command,string[] args)
{
 try
 {
     var start=new ProcessStartInfo(command){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
     foreach(var arg in args)start.ArgumentList.Add(arg);
     using var process=Process.Start(start);
     if(process is null)return false;
     using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));
     var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);
     var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
     await process.WaitForExitAsync(timeout.Token);
     await Task.WhenAll(stdout,stderr);
     return process.ExitCode==0;
 }
 catch(Exception e) when(e is System.ComponentModel.Win32Exception or OperationCanceledException or IOException){return false;}
}

static void CopyDirectory(string source,string target)
{
 Directory.CreateDirectory(target);
 foreach(var file in Directory.GetFiles(source))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
 foreach(var sub in Directory.GetDirectories(source))CopyDirectory(sub,Path.Combine(target,Path.GetFileName(sub)));
}
static void ValidateDependencies(IReadOnlySet<string> roles, IReadOnlyDictionary<string,string> flags)
{
 // Fail before generating any files or launching Docker. Cluster manifests are checked as a whole.
 if(roles.Contains("global") && !roles.Contains("orchestra") && !flags.ContainsKey("orchestra-url"))
    throw new ArgumentException("Global requires Orchestra (select Orchestra or provide --orchestra-url).");
 if(roles.Contains("local") && !roles.Contains("global") && !roles.Contains("audit") && !flags.ContainsKey("global-url") && !flags.ContainsKey("audit-url"))
    throw new ArgumentException("Local requires Global or Audit (add role or upstream URL).");
 if(roles.Contains("agent") && !roles.Contains("local") && !roles.Contains("audit") && !flags.ContainsKey("local-url") && !flags.ContainsKey("audit-url"))
    throw new ArgumentException("Agent requires Local or Audit.");
 if(roles.Contains("vpn") && !roles.Contains("global") && !roles.Contains("orchestra") && !flags.ContainsKey("global-url") && !flags.ContainsKey("orchestra-url"))
    throw new ArgumentException("3x-ui VPN node requires Global or Orchestra administration.");
 foreach(var name in new[]{"auth-url","orchestra-url","global-url","local-url","audit-url"})
  if(flags.TryGetValue(name,out var url)&&(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!="https" || uri.UserInfo.Length!=0 || uri.Fragment.Length!=0))
    throw new ArgumentException("External upstream "+name+" must use HTTPS.");
}
static async Task<bool> VerifyExternalUpstreams(string[] roles,IReadOnlyDictionary<string,string> flags)
{
 // Every declared external dependency must be alive and identify itself as the expected role.
 // HTTP health is not proof of trust: TLS and service enrolment are additionally required for production.
 var requested=new[]{("auth-url","", "/health"),("orchestra-url","Orchestra","/health/live"),
     ("global-url","Global","/health/live"),("local-url","Local","/health/live"),
     ("audit-url","AuditOrchestra","/health/live")};
 using var handler=new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false};
 using var client=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(6)};
 foreach(var (key,kind,path) in requested)
 {
   if(!flags.TryGetValue(key,out var url))continue;
   try
   {
     var uri=new Uri(url.TrimEnd('/')+path);
     using var response=await client.GetAsync(uri);
     if(!response.IsSuccessStatusCode){Console.Error.WriteLine(key+": HTTP "+(int)response.StatusCode);return false;}
     if(kind.Length>0)
     {
       using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
       if(!json.RootElement.TryGetProperty("kind",out var actual) ||
          !string.Equals(actual.GetString(),kind,StringComparison.OrdinalIgnoreCase))
       {Console.Error.WriteLine(key+": service kind does not match "+kind);return false;}
     }
   }
   catch(Exception e) when(e is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
   {Console.Error.WriteLine(key+": health check failed ("+e.GetType().Name+")");return false;}
 }
 return true;
}
static void CheckNodeLinks(DeploymentTarget node,IReadOnlySet<string> roles)
{
 if(!roles.Contains("auth") && roles.Any(x=>x is not ("vpn" or "desktop")) && string.IsNullOrEmpty(node.AuthUrl))
    throw new ArgumentException($"Node {node.Host} needs --auth-url (or Auth role on same machine).");
 if(roles.Contains("global") && !roles.Contains("orchestra") && string.IsNullOrEmpty(node.OrchestraUrl))
    throw new ArgumentException($"Node {node.Host}: Global requires Orchestra HTTPS URL.");
 if(roles.Contains("local") && !roles.Contains("global") && !roles.Contains("audit") && string.IsNullOrEmpty(node.GlobalUrl) && string.IsNullOrEmpty(node.AuditUrl))
    throw new ArgumentException($"Node {node.Host}: Local requires Global/Audit HTTPS URL.");
 if(roles.Contains("agent") && !roles.Contains("local") && !roles.Contains("audit") && string.IsNullOrEmpty(node.LocalUrl) && string.IsNullOrEmpty(node.AuditUrl))
    throw new ArgumentException($"Node {node.Host}: Agent requires Local/Audit HTTPS URL.");
 if(roles.Contains("vpn") && !roles.Contains("global") && !roles.Contains("orchestra") && string.IsNullOrEmpty(node.OrchestraUrl) && string.IsNullOrEmpty(node.GlobalUrl))
    throw new ArgumentException($"Node {node.Host}: VPN panel must be managed by Global or Orchestra.");
 foreach(var url in new[]{node.AuthUrl,node.OrchestraUrl,node.GlobalUrl,node.LocalUrl,node.AuditUrl}.Where(x=>!string.IsNullOrEmpty(x)))
  if(!Uri.TryCreate(url,UriKind.Absolute,out var uri) || uri.Scheme!=Uri.UriSchemeHttps || uri.UserInfo.Length>0 || uri.Fragment.Length>0)throw new ArgumentException("Cluster upstreams require HTTPS without userinfo or fragments.");
}
static string Compose(string[] roles,string root,string output,IReadOnlyDictionary<string,string> flags)
{
 var text=new StringBuilder("# Generated by the single MysticEgo.Installer; default binds localhost only.\nservices:\n");
 bool auth=roles.Contains("auth");
 if(auth)
 {
  text.Append("  db:\n    image: postgres:17\n    restart: unless-stopped\n    environment:\n      POSTGRES_DB: mysticego\n      POSTGRES_USER: mysticego\n      POSTGRES_PASSWORD: ${MYSTIC_DB_PASSWORD}\n    volumes:\n      - pgdata:/var/lib/postgresql/data\n    healthcheck:\n      test: [\"CMD-SHELL\", \"pg_isready -U mysticego -d mysticego\"]\n      interval: 10s\n      timeout: 3s\n      retries: 8\n");
  text.Append("  auth:\n    build:\n      context: ").Append(JsonSerializer.Serialize(root)).Append("\n      dockerfile: core/Api.Auth/Dockerfile\n    restart: unless-stopped\n    ports:\n      - '127.0.0.1:5400:8080'\n    environment:\n      ASPNETCORE_URLS: http://0.0.0.0:8080\n      Authorization__ConnectionString: Host=db;Database=mysticego;Username=mysticego;Password=${MYSTIC_DB_PASSWORD}\n      Authorization__Jwt__PrivateKeyPath: /secrets/private.pem\n      Authorization__Jwt__PublicKeyPath: /secrets/public.pem\n      Authorization__Bootstrap__Login: ").Append(InstallerProfiles.Get(flags)=="test"?"developer":"owner").Append("\n      Authorization__Bootstrap__Password: ${MYSTIC_ADMIN_PASSWORD}\n    volumes:\n      - ").Append(JsonSerializer.Serialize(Path.Combine(output,"secrets"))).Append(":/secrets:ro\n    depends_on:\n      db:\n        condition: service_healthy\n");
 }
 var ports=new Dictionary<string,int>{{"orchestra",5500},{"global",5100},{"local",5200},{"agent",5300},{"audit",5350}};
 foreach(var role in roles.Where(x=>x is not ("auth" or "vpn")))
 {
  var name=role=="audit"?"AuditOrchestra":char.ToUpperInvariant(role[0])+role[1..];
  text.Append("  ").Append(role).Append(":\n    build:\n      context: ").Append(JsonSerializer.Serialize(root)).Append("\n      dockerfile: services/Api.").Append(name).Append("/Dockerfile\n    restart: unless-stopped\n    ports:\n      - '127.0.0.1:").Append(ports[role]).Append(":8080'\n    environment:\n      ASPNETCORE_URLS: http://0.0.0.0:8080\n      Authorization__Jwt__PublicKeyPath: /secrets/public.pem\n      Node__DeploymentMode: single-machine\n      Node__AllowInsecureTestHttp: 'true'\n      Node__AuthUrl: ").Append(JsonSerializer.Serialize(flags.GetValueOrDefault("auth-url")??"http://auth:8080")).Append("\n      Avatar__SyncKey: ${MYSTIC_AVATAR_SYNC_KEY}\n      Vpn__LinkEncryptionKey: ${MYSTIC_VPN_LINK_KEY}\n      Avatar__StoragePath: /data/avatar\n      Avatar__ReplicaPath: /data/replica\n");
  if(role=="global")text.Append("      Avatar__OrganizationId: default\n");
  if(role=="orchestra")text.Append("      WorldAI__StoragePath: /data/world-ai\n");
  if(flags.TryGetValue("orchestra-url",out var orchestration))text.Append("      Node__OrchestraUrl: ").Append(JsonSerializer.Serialize(orchestration)).Append("\n");
  else if(roles.Contains("orchestra") && role is ("global" or "audit"))text.Append("      Node__OrchestraUrl: http://orchestra:8080\n");
  if(role=="local")
  {
    var upstream=flags.GetValueOrDefault("global-url")??(roles.Contains("global")?"http://global:8080":flags.GetValueOrDefault("audit-url")??"http://audit:8080");
    text.Append("      Avatar__UpstreamUrl: ").Append(JsonSerializer.Serialize(upstream)).Append("\n      Avatar__AllowInsecureLocalSync: 'true'\n");
    if(flags.TryGetValue("global-url",out var g))text.Append("      Node__GlobalUrl: ").Append(JsonSerializer.Serialize(g)).Append('\n');
    else if(roles.Contains("global"))text.Append("      Node__GlobalUrl: http://global:8080\n");
    else text.Append("      Node__AuditUrl: ").Append(JsonSerializer.Serialize(flags.GetValueOrDefault("audit-url")??"http://audit:8080")).Append('\n');
  }
  if(role=="agent")
  {
    var upstream=flags.GetValueOrDefault("local-url")??(roles.Contains("local")?"http://local:8080":flags.GetValueOrDefault("audit-url")??"http://audit:8080");
    text.Append("      Avatar__UpstreamUrl: ").Append(JsonSerializer.Serialize(upstream)).Append("\n      Avatar__AllowInsecureLocalSync: 'true'\n");
    if(flags.TryGetValue("global-url",out var g))text.Append("      Node__GlobalUrl: ").Append(JsonSerializer.Serialize(g)).Append('\n');
    else if(roles.Contains("global"))text.Append("      Node__GlobalUrl: http://global:8080\n");
    if(flags.TryGetValue("local-url",out var l))text.Append("      Node__LocalUrl: ").Append(JsonSerializer.Serialize(l)).Append('\n');
    else if(roles.Contains("local"))text.Append("      Node__LocalUrl: http://local:8080\n");
    else text.Append("      Node__AuditUrl: ").Append(JsonSerializer.Serialize(flags.GetValueOrDefault("audit-url")??"http://audit:8080")).Append('\n');
  }
  if(role is "local" or "agent" or "audit")text.Append("      Node__IdentityId: ${").Append(role.ToUpperInvariant()).Append("_IDENTITY_ID}\n      Node__IdentitySecret: ${").Append(role.ToUpperInvariant()).Append("_IDENTITY_SECRET}\n");
  text.Append("    volumes:\n      - ").Append(JsonSerializer.Serialize(Path.Combine(output,"secrets"))).Append(":/secrets:ro\n      - ").Append(role).Append("-data:/data\n");
 }
 if(roles.Contains("vpn"))
 {
  // 3x-ui panel is intentionally not exposed to the public Internet from this Compose profile.
  // Inbounds, certificates, authentication and reverse proxy are configured by the operator.
  text.Append("  vpn:\n    image: ghcr.io/mhsanaei/3x-ui:latest\n    restart: unless-stopped\n    ports:\n      - '127.0.0.1:2053:2053'\n    volumes:\n      - vpn-data:/etc/x-ui\n      - vpn-cert:/root/cert\n");
 }
 text.Append("volumes:\n");if(auth)text.Append("  pgdata:\n");
 if(roles.Contains("vpn"))text.Append("  vpn-data:\n  vpn-cert:\n");
 foreach(var role in roles.Where(x=>x is not ("auth" or "vpn")))text.Append("  ").Append(role).Append("-data:\n");
 return text.ToString();
}

public sealed record DeploymentPlan(DeploymentTarget[] Nodes);
public sealed record DeploymentTarget(string Host,string[] Roles,string? AuthUrl=null,string? OrchestraUrl=null,string? GlobalUrl=null,string? LocalUrl=null,string? AuditUrl=null);
