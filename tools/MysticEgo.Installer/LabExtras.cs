using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MysticEgo.Deployment;

internal static class LabExtras
{
    internal static async Task Desktop(string root)
    {
        if (OperatingSystem.IsWindows())
        {
            var runtime = Path.Combine(root, "runtime", "electron", "electron.exe");
            if (!File.Exists(runtime)) throw new IOException("Распакуйте полный архив с runtime/electron.");
            await LabPackage.RunTool(runtime, [Path.Combine(root, "desktop")], root);
            return;
        }
        if(!await InstallerDependencies.EnsureAsync(false,true,true))throw new IOException("Не удалось подготовить Node.js.");
        var dir=Path.Combine(root,"desktop");
        var installed=Path.Combine(dir,"node_modules","electron","dist",OperatingSystem.IsWindows()?"electron.exe":"electron");
        if(!File.Exists(installed))
            await LabPackage.RunTool(OperatingSystem.IsWindows()?"cmd.exe":"npm",
                OperatingSystem.IsWindows()?["/d","/c","npm","ci","--no-audit","--no-fund"]:["ci","--no-audit","--no-fund"],dir);
        if(!File.Exists(installed))await LabPackage.RunTool("node",[Path.Combine(dir,"node_modules","electron","install.js")],dir);
        await LabPackage.RunTool("node",[Path.Combine(dir,"node_modules","electron","cli.js"),"."],dir);
    }
    internal static async Task Verify(Dictionary<string,string> flags)
    {
        var root=flags.GetValueOrDefault("output")??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MysticEgo","BETA7-Lab","center");
        using var http=new HttpClient(new HttpClientHandler{UseProxy=false,AllowAutoRedirect=false}){Timeout=TimeSpan.FromSeconds(105)};
        foreach(var (role,service) in LabPackage.Roles)
        {
            var path=role=="auth"?"/health":"/health/live";
            var result=await http.GetFromJsonAsync<JsonElement>($"http://127.0.0.1:{service.Port}{path}");
            if(role!="auth" && result.GetProperty("kind").GetString()!=service.Project[4..])throw new IOException("Вместо "+role+" отвечает другой API.");
            Console.WriteLine(role+": OK");
        }
        using var login=await http.PostAsJsonAsync("http://127.0.0.1:5400/api/auth/login",new{login="developer",password=File.ReadAllText(Path.Combine(root,"owner-password.txt")).Trim()});
        login.EnsureSuccessStatusCode();
        var auth=await login.Content.ReadFromJsonAsync<JsonElement>();
        http.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",auth.GetProperty("accessToken").GetString());
        var profile=JsonSerializer.Deserialize<StationProfile>(File.ReadAllText(Path.Combine(root,"center-station.json")),new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var id=profile.Credentials.Single(x=>x.Kind=="agent").IdentityId;
        foreach(var port in new[]{5200,5100})
        {
            var api=$"http://127.0.0.1:{port}/api/v1";
            var caps=await http.GetFromJsonAsync<JsonElement>(api+$"/agents/{id}/capabilities");
            if(!caps.EnumerateArray().Any(c=>c.GetProperty("id").GetString()=="system.info"))throw new IOException($"На порту {port} пока нет каталога Agent.");
            using var r=await http.PostAsJsonAsync(api+"/commands/dispatch",new{targetId=id,commandId="system.info",rawArgs="",timeoutSeconds=15});
            r.EnsureSuccessStatusCode();var execution=await r.Content.ReadFromJsonAsync<JsonElement>();
            if(!execution.GetProperty("success").GetBoolean())throw new IOException("Agent не подтвердил выполнение system.info.");
            Console.WriteLine($"{port} → Agent → system.info: OK ({caps.GetArrayLength()} команд)");
        }
        if(flags.ContainsKey("with-lm")){
            using var r=await http.PostAsJsonAsync("http://127.0.0.1:5300/api/v1/assistant/chat",new{text="Reply OK /no_think"});
            r.EnsureSuccessStatusCode();var v=await r.Content.ReadFromJsonAsync<JsonElement>();
            if(string.IsNullOrWhiteSpace(v.GetProperty("answer").GetString()))throw new IOException("Модель вернула пустой ответ.");
            Console.WriteLine("Agent → LM Studio: OK");
        }
    }
}
