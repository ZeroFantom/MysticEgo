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
        // Status is published by the existing root-owned updater. No package is modified here.
        JsonObject? update=null;
        try
        {
            var status=await SSH(args,"test -f /var/lib/mysticego-orchestra-updater/status.json && test $(stat -c %s /var/lib/mysticego-orchestra-updater/status.json) -le 16384 && cat /var/lib/mysticego-orchestra-updater/status.json",1);
            update=JsonNode.Parse(status) as JsonObject;
        }
        catch(Exception e) when(e is not OperationCanceledException) { }
        var candidate=update?["candidate"] as JsonObject;
        var identity=JsonNode.Parse(await SSH(args,"curl -fsS --max-time 5 http://127.0.0.1:5500/health/live",1)) as JsonObject;
        if(identity is null||S(identity,"status")!="alive"||S(identity,"kind").ToLowerInvariant()!="orchestra"||!Guid.TryParse(S(identity,"node"),out var node))
            throw new Exception("Не подтверждён ID Orchestra.");
        var serviceState=await SSH(args,"if systemctl is-active --quiet mysticego-local-setup.socket && systemctl is-active --quiet mysticego-infrastructure-reconcile.timer && systemctl is-active --quiet mysticego-orchestra-updater.timer; then echo ready; else echo missing; fi",1);
        var ready=update is not null && S(update,"phase") is ("ready" or "failed") &&
            candidate is not null && ValidUpdateCandidate(S(candidate,"tag"),S(candidate,"digest")) &&
            S(candidate,"asset")=="MysticEgo-Orchestra-linux-x64.zip" &&
            (update["targets"] as JsonArray)?.Any(t=>t?.ToString()=="orchestra")==true;
        return new {domain,node=node.ToString("D"),serviceState,host=S(args,"host"),serverId=Num(args,"serverId"),current=update is null?"":S(update,"current"),
            updatePhase=update is null?"unavailable":S(update,"phase"),candidateTag=ready?S(candidate!,"tag"):"",candidateDigest=ready?S(candidate!,"digest"):""};
    }

    /// <summary>Read the shared owner-only plan over HTTPS; never poll privileged SSH in a loop.</summary>
    async Task<object> ReadMaintenancePlan(JsonObject args)
    {
        var origin=TopologyOrigin("https://"+Need(args,"domain",DnsNamePattern));
        var root=await RequireDeveloper(origin,ownerOnly:true);
        if(!Guid.TryParse(root,out var id)||!Guid.TryParse(S(args,"node"),out var expected)||id!=expected)throw new Exception("Сессия относится к другой Orchestra.");
        await topologyGate.WaitAsync();
        try
        {
            if(topologyOrigin!=origin||topologyRootId!=root||topologyAccess.Length==0)throw new Exception("Сессия изменена.");
            try{return await TopologyRequest(origin,"/api/v1/orchestra/update/plan",topologyAccess);}
            catch(HttpRequestException e) when(e.StatusCode==HttpStatusCode.NotFound)
            {
                var update=await TopologyRequest(origin,"/api/v1/orchestra/update",topologyAccess);
                var report=await TopologyRequest(origin,"/api/v1/managed/infrastructure-maintenance",topologyAccess);
                var candidate=update["candidate"];
                var available=candidate is JsonObject c&&ValidUpdateCandidate(S(c,"tag"),S(c,"digest"))&&S(c,"tag")!=update["current"]?.ToString()&&
                    (update["targets"] as JsonArray)?.Any(x=>x?.ToString()=="orchestra")==true&&update["phase"]?.ToString() is ("ready" or "failed");
                var missing=report["socketAvailable"]?.GetValue<bool>()!=true;
                var action=update["phase"]?.ToString() is ("switching" or "downloading")?"busy":available?"update":missing?"legacy-repair":"unknown";
                return new{action,reason=action=="update"?"Доступен новый релиз":action=="legacy-repair"?"Не найден сокет обслуживания":"Старый сервер: ожидается проверка или новый релиз.",update,components=report["components"],services=Array.Empty<object>(),legacy=true};
            }
        }
        finally{topologyGate.Release();}
    }

    async Task<object> ExecuteMaintenancePlan(JsonObject args,Action<string> progress)
    {
        if(S(args,"confirm")!="MAINTENANCE_PLAN")throw new Exception("Подтвердите обслуживание.");
        var action=S(args,"planAction");
        var domain=await InstalledOrchestraDomain(args);
        if(domain!=S(args,"domain"))throw new Exception("Домен изменился.");
        var plan=System.Text.Json.JsonSerializer.SerializeToNode(await ReadMaintenancePlan(args))!.AsObject();
        if(S(plan,"action")!=action)throw new Exception("Состояние изменилось. Повторите проверку.");
        if(action is "legacy-repair" or "unavailable")
        {
            var copy=(JsonObject)args.DeepClone();copy["confirm"]="RECOVER_MAINTENANCE";
            return await RecoverOrchestraMaintenance(copy,progress);
        }
        if(action=="update"&&Bool(plan,"legacy",false))
        {
            var copy=(JsonObject)args.DeepClone();copy["confirm"]="RESTORE_ORCHESTRA";
            return await RestoreOrchestraUpdater(copy,progress);
        }
        if(action is not ("repair" or "update"))throw new Exception("Обслуживание сейчас не требуется.");
        var origin=TopologyOrigin("https://"+domain);
        var root=await RequireDeveloper(origin,ownerOnly:true);
        await topologyGate.WaitAsync();
        try
        {
            if(topologyOrigin!=origin||topologyRootId!=root||topologyAccess.Length==0||root!=S(args,"node"))throw new Exception("Сессия изменена.");
            var result=await TopologyRequest(origin,"/api/v1/orchestra/update/plan",topologyAccess,new{action,confirmed=true,tag=S(args,"candidateTag"),digest=S(args,"candidateDigest")});
            return new{queued=true,result};
        }
        finally{topologyGate.Release();}
    }

    /// <summary>Export only owner-authorized sanitized reports from the bound Orchestra.</summary>
    async Task<object> MaintenanceReportDownload(JsonObject args)
    {
        var format=Need(args,"format",@"\A(?:xlsx|json|txt)\z");
        var scope=Need(args,"scope",@"\A[a-zA-Z0-9@_.-]{1,80}\z");
        var origin=TopologyOrigin("https://"+Need(args,"domain",DnsNamePattern));
        var root=await RequireDeveloper(origin,ownerOnly:true);
        if(!Guid.TryParse(root,out var id)||!Guid.TryParse(S(args,"node"),out var expected)||id!=expected)throw new Exception("Сессия относится к другой Orchestra.");
        await topologyGate.WaitAsync();
        try
        {
            if(topologyOrigin!=origin||topologyRootId!=root||topologyAccess.Length==0)throw new Exception("Сессия изменена.");
            using var request=new HttpRequestMessage(HttpMethod.Get,origin+"/api/v1/orchestra/diagnostics/report?format="+format+"&scope="+Uri.EscapeDataString(scope));
            request.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",topologyAccess);
            using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(25));
            using var response=await topologyHttp.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            response.EnsureSuccessStatusCode();
            using var input=await response.Content.ReadAsStreamAsync(timeout.Token);
            using var output=new MemoryStream();var chunk=new byte[8192];int count;
            while((count=await input.ReadAsync(chunk.AsMemory(),timeout.Token))>0)
            {if(output.Length+count>4*1024*1024)throw new Exception("Отчёт превышает лимит 4 MiB. Выберите отдельную службу.");output.Write(chunk,0,count);}
            return new{content=Convert.ToBase64String(output.ToArray()),format,scope};
        }
        finally{topologyGate.Release();}
    }

    async Task<object> ConfigureMaintenanceAuto(JsonObject args)
    {
        if(S(args,"confirm")!="AUTO_UPDATE"||args["enabled"] is not JsonValue enabled||!enabled.TryGetValue<bool>(out var value))
            throw new Exception("Подтвердите политику обновлений.");
        var origin=TopologyOrigin("https://"+Need(args,"domain",DnsNamePattern));
        var root=await RequireDeveloper(origin,ownerOnly:true);
        if(!Guid.TryParse(root,out var id)||!Guid.TryParse(S(args,"node"),out var expected)||id!=expected)throw new Exception("Сессия относится к другой Orchestra.");
        await topologyGate.WaitAsync();
        try
        {
            if(topologyOrigin!=origin||topologyRootId!=root||topologyAccess.Length==0)throw new Exception("Сессия изменена.");
            return await TopologyRequest(origin,"/api/v1/orchestra/update/automatic",topologyAccess,new{enabled=value,confirmed=true});
        }
        finally{topologyGate.Release();}
    }

    /// <summary>Only exact published project releases with a SHA-256 digest can be approved.</summary>
    static bool ValidUpdateCandidate(string tag,string digest)=>
        System.Text.RegularExpressions.Regex.IsMatch(tag,@"\Av\d+\.\d+\.\d+-beta\.\d+-r\d+(?:\.\d+){0,7}\z")&&
        System.Text.RegularExpressions.Regex.IsMatch(digest,@"\Asha256:[a-f0-9]{64}\z");

    static object UpdateCandidateSelftest()
    {
        var tag="v1.0.0-beta.7-r3.2.57";var digest="sha256:"+new string('a',64);
        if(!ValidUpdateCandidate(tag,digest)||ValidUpdateCandidate(tag+"\n",digest)||ValidUpdateCandidate("../main",digest)||
           ValidUpdateCandidate(tag,"sha256:bad"))throw new Exception("Update candidate validation failed");
        return new{passed=4};
    }

    async Task<object> RecoverOrchestraMaintenance(JsonObject args, Action<string> progress)
    {
        if (S(args,"confirm")!="RECOVER_MAINTENANCE" || S(args,"user")!="root")
            throw new Exception("Подтвердите восстановление службы обслуживания.");
        var domain=await InstalledOrchestraDomain(args);
        if(!domain.Equals(S(args,"domain"),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Домен установки изменился. Проверьте VPS повторно.");
        var health=await SSH(args,"curl -fsS --max-time 5 http://127.0.0.1:5500/health/live",1);
        var response=JsonNode.Parse(health);
        if(response is not JsonObject info || S(info,"status")!="alive" || !S(info,"kind").Equals("orchestra",StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(S(info,"node"),out var node) || node==Guid.Empty)
            throw new Exception("Не удалось подтвердить ID действующей Orchestra.");
        var owner=await RequireDeveloper(TopologyOrigin("https://"+domain),ownerOnly:true);
        if(!Guid.TryParse(owner,out var ownerNode)||ownerNode!=node||!Guid.TryParse(S(args,"node"),out var selectedNode)||selectedNode!=node)
            throw new Exception("Сессия владельца или выбранный узел не совпадает с VPS.");
        var script=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","ops","orchestra-updater","recover-maintenance.sh"));
        if(!File.Exists(script))throw new Exception("Распакуйте актуальный полный архив MysticEgo.");
        var digest=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))).ToLowerInvariant();
        var remote="/root/.clouddesk-maintenance-"+Guid.NewGuid().ToString("N");
        progress("Проверен сервер Orchestra. Приложение и данные не заменяются.");
        await SSH(args,"umask 077; mkdir -m 700 "+remote,1);
        try {
            await Upload(args,script,remote+"/recover-maintenance.sh");
            progress("Проверка SHA-256 и восстановление двух системных служб");
            var command="cd "+remote+" && printf '%s  recover-maintenance.sh\\n' "+digest+
                " | sha256sum -c - && bash recover-maintenance.sh "+node.ToString("D");
            var output=await SSH(args,command,10,line=>progress(Redact(line)));
            if(!output.Split('\n').Any(line=>line.Trim()=="MAINTENANCE_RECOVERY_OK"))
                throw new Exception("Служба не подтвердила восстановление.");
            progress("Сокет и таймеры активны. Orchestra отвечает с прежним ID.");
            return new {domain,recovered=true,maintenanceScheduled=true,output="Службы обновления и обслуживания восстановлены; проверки компонентов выполняются автоматически."};
        } finally {
            try {await SSH(args,"rm -rf -- "+remote,1);}catch {}
        }
    }

    /// <summary>Submit the exact release selected by the owner to the existing transactional update worker.
    /// Never copy partial DLL sets, downgrade, or initiate a second independent update.</summary>
    async Task<object> RestoreOrchestraUpdater(JsonObject args, Action<string> progress)
    {
        if(S(args,"confirm")!="RESTORE_ORCHESTRA")throw new Exception("Подтвердите обновление Orchestra.");
        var tag=S(args,"candidateTag");var digest=S(args,"candidateDigest");
        if(!ValidUpdateCandidate(tag,digest))throw new Exception("Сначала определите доступный релиз Orchestra и подтвердите его версию.");
        var domain=await InstalledOrchestraDomain(args);
        if(!domain.Equals(S(args,"domain"),StringComparison.OrdinalIgnoreCase))
            throw new Exception("Домен установки изменился. Проверьте VPS повторно.");
        var info=JsonNode.Parse(await SSH(args,"curl -fsS --max-time 5 http://127.0.0.1:5500/health/live",1)) as JsonObject;
        if(info is null||S(info,"status")!="alive"||!S(info,"kind").Equals("orchestra",StringComparison.OrdinalIgnoreCase)||
            !Guid.TryParse(S(info,"node"),out var node)||node==Guid.Empty)throw new Exception("Не подтверждён ID Orchestra по SSH.");
        var origin=TopologyOrigin("https://"+domain);
        progress("Проверка сессии владельца Orchestra");
        await RequireDeveloper(origin,ownerOnly:true);
        await topologyGate.WaitAsync();
        try
        {
            if(topologyOrigin!=origin||topologyAccess.Length==0||!Guid.TryParse(topologyRootId,out var root)||root!=node)
                throw new Exception("Сессия относится к другой Orchestra.");
            var status=(await TopologyRequest(origin,"/api/v1/orchestra/update",topologyAccess)).AsObject();
            var candidate=status["candidate"] as JsonObject;
            if(S(status,"phase") is not ("ready" or "failed")||candidate is null||
                S(candidate,"tag")!=tag||S(candidate,"digest")!=digest||
                S(candidate,"asset")!="MysticEgo-Orchestra-linux-x64.zip"||
                (status["targets"] as JsonArray)?.Any(x=>x?.ToString()=="orchestra")!=true)
                throw new Exception("Релиз изменился или недоступен. Обновите сведения о сервере; повторно подтвердите версию.");
            progress("Передача подтверждённого релиза штатной службе обновления: "+tag);
            var response=(await TopologyRequest(origin,"/api/v1/orchestra/update",topologyAccess,new{tag,digest,confirmed=true,target="orchestra"})).AsObject();
            if(!Bool(response,"accepted",true))throw new Exception("Orchestra не подтвердила постановку обновления в очередь.");
            return new {domain,tag,queued=true,output="Обновление поставлено в очередь. Проверьте результат в панели Orchestra; установка ещё не подтверждена."};
        }
        finally{topologyGate.Release();}
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

