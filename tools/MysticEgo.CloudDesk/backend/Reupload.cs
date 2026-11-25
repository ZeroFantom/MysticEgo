using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

sealed partial class CloudDesk {
    async Task VerifyReuploadTarget(JsonObject args) {
        var host=Need(args,"host",@"^[0-9.]+$");
        var ips=await PublicIps(Num(args,"serverId"));
        if(!PublicAddress(host,"ipv4") || !ips.OfType<JsonObject>().Any(x=>S(x,"type")=="ipv4" && S(x,"ip")==host))
            throw new Exception("IPv4 больше не принадлежит указанному VPS Timeweb.");
    }

    async Task<string> InstalledOrchestraDomain(JsonObject args)
    {
        if(S(args,"user")!="root")throw new Exception("Для проверки нужен доверенный SSH root.");
        await VerifyReuploadTarget(args);
        var origin=await SSH(args,"systemctl is-active --quiet mysticego-orchestra.service && sed -n 's/^MYSTICEGO_CENTER_ORCHESTRA_URL=//p' /etc/mysticego/orchestra.env",1);
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo!=""||
            uri.Query!=""||uri.Fragment!=""||uri.AbsolutePath!="/"||!uri.IsDefaultPort||
            !System.Text.RegularExpressions.Regex.IsMatch(uri.Host,DnsNamePattern))
            throw new Exception("На выбранном VPS не найден действующий HTTPS-домен Orchestra.");
        return uri.Host;
    }

    async Task<object> InspectOrchestra(JsonObject args)
    {
        var domain=await InstalledOrchestraDomain(args);
        return new {domain,host=S(args,"host"),serverId=Num(args,"serverId")};
    }

    async Task<object> RestoreOrchestraUpdater(JsonObject args, Action<string> progress) {
        if(S(args,"confirm")!="RESTORE_ORCHESTRA")throw new Exception("Подтвердите обновление Orchestra.");
        if(S(args,"user")!="root")throw new Exception("Для установки обновлятора нужен доверенный SSH root.");
        var domain=await InstalledOrchestraDomain(args);
        if(!domain.Equals(S(args,"domain"),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Домен установки изменился. Проверьте VPS повторно.");
        var source=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","ops","orchestra-updater"));
        string[] required=["bootstrap-r31.sh","baseline.sha256","install-worker.sh","update-orchestra.py",
            "mysticego-orchestra-updater.service","mysticego-orchestra-updater.timer","payload/Api.Orchestra.dll",
            "payload/MysticEgo.WorldAI.dll","payload/MysticEgo.Installer.dll","payload/web/index.html","payload/web/release-info.json","payload/web/favicon.ico","payload/web/mystic-mark.png"];
        var defaultModel=new Dictionary<string,string> {
            ["model/install-default-model.sh"]="../panel-assistant/install-default-model.sh",
            ["model/infrastructure/install.sh"]="../infrastructure/install.sh",
            ["model/infrastructure/install.py"]="../infrastructure/install.py",
            ["model/infrastructure/install-local.sh"]="../infrastructure/install-local.sh",
            ["model/infrastructure/local-runner.py"]="../infrastructure/local-runner.py"
        };
        if(required.Concat(defaultModel.Values).Any(file=>!File.Exists(Path.Combine(source,file))))throw new Exception("Распакуйте полный архив с обновлятором Orchestra.");
        var assets=Directory.GetFiles(Path.Combine(source,"payload","web","assets"));
        if(assets.Length==0)throw new Exception("В архиве отсутствует собранная панель Orchestra.");
        var local=Directory.CreateTempSubdirectory("mysticego-restore-").FullName;
        var remote="/root/.clouddesk-restore-"+Guid.NewGuid().ToString("N");
        try {
            var zip=Path.Combine(local,"updater.zip");
            using(var package=ZipFile.Open(zip,ZipArchiveMode.Create)) {
                foreach(var file in required)package.CreateEntryFromFile(Path.Combine(source,file),file);
                foreach(var file in defaultModel)package.CreateEntryFromFile(Path.Combine(source,file.Value),file.Key);
                foreach(var file in assets)package.CreateEntryFromFile(file,"payload/web/assets/"+Path.GetFileName(file));
            }
            using var stream=File.OpenRead(zip);
            var digest=Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
            progress("Проверка установленной Orchestra и её домена");
            await SSH(args,"test \"$(id -u)\" = 0 && grep -Fxq "+ShellQuote("MYSTICEGO_CENTER_ORCHESTRA_URL=https://"+domain)+" /etc/mysticego/orchestra.env && systemctl is-active --quiet mysticego-orchestra.service && curl -fsS --max-time 5 http://127.0.0.1:5500/health/live >/dev/null",1);
            await SSH(args,"umask 077; mkdir -m 700 "+remote,1);
            try {
                progress("Передача обновлятора Orchestra через SCP");
                await Upload(args,zip,remote+"/updater.zip");
                progress("Проверка пакета, установка и проверка health");
                var command="cd "+remote+" && printf '%s  updater.zip\\n' "+digest+" | sha256sum -c - && unzip -q updater.zip && (command -v gh >/dev/null || (apt-get update && apt-get install -y gh)) && bash bootstrap-r31.sh";
                var output=await SSH(args,"flock -n /run/lock/mysticego-clouddesk-deploy.lock bash -c "+ShellQuote(command),12);
                return new {domain,output};
            } finally {
                try { await SSH(args,"rm -rf -- "+remote,1); } catch { }
            }
        } finally { Directory.Delete(local,true); }
    }

    async Task<JsonNode> QueueGlobalReupload(JsonObject args) {
        if(S(args,"confirm")!="REUPLOAD_GLOBAL")throw new Exception("Подтвердите перезаливку Global.");
        await VerifyReuploadTarget(args);
        if(!Guid.TryParse(S(args,"nodeId"),out var id)||id==Guid.Empty)throw new Exception("Выберите зарегистрированный Global.");
        var origin=TopologyOrigin(S(args,"origin"));
        await RequireDeveloper(origin);
        await topologyGate.WaitAsync();
        try {
            if(topologyOrigin!=origin || topologyAccess.Length==0)throw new Exception("Сессия Orchestra изменилась.");
            var access=await TopologyRequest(origin,$"/api/v1/managed/access/{id}",topologyAccess);
            if(access is not JsonObject rights||!Bool(rights,"canManage",true))throw new Exception("Нет права управлять этим Global.");
            var topology=await TopologyRequest(origin,"/api/v1/managed/topology",topologyAccess) as JsonArray;
            var node=topology?.Select(row=>row?["node"]).OfType<JsonObject>().FirstOrDefault(n=>Guid.TryParse(S(n,"id"),out var nodeId)&&nodeId==id);
            if(node is null||!S(node,"kind").Equals("global",StringComparison.OrdinalIgnoreCase))
                throw new Exception("Узел не зарегистрирован как Global.");
            var url=new Uri(S(node,"publicUrl"));
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var addresses=await Dns.GetHostAddressesAsync(url.Host,timeout.Token);
            if(!addresses.Any(ip=>ip.ToString()==S(args,"host")))throw new Exception("DNS Global не указывает на выбранный VPS.");
            Target(args);
            var target=new {host=S(args,"host"),user=S(args,"user"),port=int.Parse(Port(args)),os="linux"};
            var check=await TopologyRequest(origin,$"/api/v1/managed/nodes/{id}/preflight",topologyAccess,target);
            if(check is not JsonObject readiness||!Bool(readiness,"ssh",true)||
                S(node,"mode")=="docker"&&!Bool(readiness,"docker",true))
                throw new Exception("Orchestra не подтвердила готовность SSH или Docker на целевом Global.");
            return await TopologyRequest(origin,$"/api/v1/managed/nodes/{id}/deploy",topologyAccess,target);
        } finally { topologyGate.Release(); }
    }
}

