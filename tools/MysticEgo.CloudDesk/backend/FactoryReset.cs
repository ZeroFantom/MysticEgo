using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed partial class CloudDesk
{
    // Never reuse the historical installer tag for a destructive reinstall.
    static readonly Regex ResetTagPattern=new(@"\Av1\.0\.0-beta\.7-r(\d{1,4})\.(\d{1,4})\.(\d{1,4})\z",RegexOptions.CultureInvariant);
    sealed record ResetRelease(string Tag,string Asset,string Digest);
    sealed record ResetApproval(string Nonce,string Domain,string Node,string Host,int ServerId,ResetRelease Release,DateTimeOffset Expires);
    ResetApproval? resetApproval;

    static Version? ResetVersion(string tag)
    {
        var m=ResetTagPattern.Match(tag);
        return m.Success?new Version(int.Parse(m.Groups[1].Value),int.Parse(m.Groups[2].Value),int.Parse(m.Groups[3].Value)):null;
    }
    static string ResetAsset(string tag)
    {
        if(ResetVersion(tag)==null)throw new Exception("Unsupported release tag.");
        return "MysticEgo_BETA7_R"+tag.Split("-r")[1]+".zip";
    }
    static string ResetNewestTag(JsonArray rows)
    {
        return rows.OfType<JsonObject>().Where(r=>r["isDraft"]?.GetValue<bool>()==false&&DateTimeOffset.TryParse(S(r,"publishedAt"),out _))
            .Select(r=>S(r,"tagName")).Where(t=>ResetVersion(t)!=null)
            .OrderByDescending(t=>ResetVersion(t)).FirstOrDefault()??throw new Exception("Нет опубликованного релиза Beta 7.");
    }
    static ResetRelease ResetReleaseFromMetadata(JsonObject release,string tag)
    {
        if(S(release,"tag_name")!=tag||release["draft"]?.GetValue<bool>()!=false||!DateTimeOffset.TryParse(S(release,"published_at"),out _))
            throw new Exception("Релиз не опубликован.");
        var asset=ResetAsset(tag);
        var file=(release["assets"] as JsonArray)?.OfType<JsonObject>().SingleOrDefault(x=>S(x,"name")==asset);
        if(file==null||S(file,"state")!="uploaded"||!long.TryParse(S(file,"size"),out var size)||size<=0||!ValidUpdateCandidate(tag,S(file,"digest")))
            throw new Exception("У актуального релиза нет проверенного полного архива.");
        return new(tag,asset,S(file,"digest")["sha256:".Length..]);
    }
    async Task<ResetRelease> LatestResetRelease()
    {
        var secret=await GithubLocalToken();
        if(!GithubRepositoryMatches(GithubRepoView(await GithubGet("repos/"+Repo,secret))))throw new Exception("Репозиторий не подтверждён.");
        var listing=JsonNode.Parse(await Exec("gh",["release","list","--repo",Repo,"--limit","100","--json","tagName,isDraft,publishedAt"],minutes:1)) as JsonArray
            ??throw new Exception("Некорректный список релизов GitHub.");
        var tag=ResetNewestTag(listing);
        return ResetReleaseFromMetadata(await GithubGet("repos/"+Repo+"/releases/tags/"+tag,secret),tag);
    }
    static bool ResetConfirmationMatches(string domain,string phrase)=>phrase=="DELETE "+domain;
    async Task<(string Domain,string Node)> ResetIdentity(JsonObject args)
    {
        var domain=await InstalledOrchestraDomain(args);
        if(domain!=S(args,"domain")||!Guid.TryParse(S(args,"node"),out var expected)||expected==Guid.Empty)
            throw new Exception("Целевой домен или ID изменился.");
        var health=JsonNode.Parse(await SSH(args,"curl -fsS --max-time 5 http://127.0.0.1:5500/health/live",1)) as JsonObject;
        if(health==null||S(health,"status")!="alive"||!S(health,"kind").Equals("orchestra",StringComparison.OrdinalIgnoreCase)||
           !Guid.TryParse(S(health,"node"),out var actual)||actual!=expected)
            throw new Exception("SSH относится к другой Orchestra.");
        var owner=await RequireDeveloper(TopologyOrigin("https://"+domain),ownerOnly:true);
        if(!Guid.TryParse(owner,out var ownerNode)||ownerNode!=expected)throw new Exception("Сессия владельца относится к другой Orchestra.");
        return (domain,expected.ToString("D"));
    }
    static string ResetScriptPath()=>Path.Combine(AppContext.BaseDirectory,"profiles","orchestra-reset.sh");
    static string ResetInstallerPath()=>Path.Combine(AppContext.BaseDirectory,"profiles","orchestra-r3.sh");
    static string ResetQ(string s)=>"'"+s.Replace("'","'\"'\"'")+"'";
    async Task<object> PreviewFactoryReset(JsonObject args)
    {
        resetApproval=null;
        var identity=await ResetIdentity(args);
        var release=await LatestResetRelease();
        var script=ResetScriptPath();
        if(!File.Exists(script)||!File.Exists(ResetInstallerPath()))throw new Exception("Обновите CloudDesk: отсутствует профиль полного сброса.");
        // The same preflight runs again under the host lock before any mutation.
        var remote="/root/.clouddesk-reset-preview-"+Guid.NewGuid().ToString("N");
        var digest=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))).ToLowerInvariant();
        await SSH(args,"umask 077; mkdir -m 700 "+remote,1);
        try {
            await Upload(args,script,remote+"/reset.sh");
            await SSH(args,"cd "+remote+" && printf '%s  reset.sh\\n' "+digest+" | sha256sum -c - && bash reset.sh check "+ResetQ(identity.Domain)+" "+ResetQ(identity.Node),2);
        } finally {try{await SSH(args,"rm -rf -- "+remote,1);}catch{}}
        var approval=new ResetApproval(Guid.NewGuid().ToString("N"),identity.Domain,identity.Node,S(args,"host"),Num(args,"serverId"),release,DateTimeOffset.UtcNow.AddMinutes(10));
        resetApproval=approval;
        return new {nonce=approval.Nonce,domain=identity.Domain,node=identity.Node,host=approval.Host,serverId=approval.ServerId,tag=release.Tag,digest=release.Digest,expiresAt=approval.Expires};
    }
    static string? DeploymentStepMessage(string line)=>line.Trim() switch
    {
        "CLOUDDESK_STEP:checksum"=>"VPS · проверка архива",
        "CLOUDDESK_STEP:apt-update"=>"VPS · обновление списка пакетов",
        "CLOUDDESK_STEP:apt-install"=>"VPS · установка системных зависимостей",
        "CLOUDDESK_STEP:unpack"=>"VPS · распаковка MysticEgo и .NET",
        "CLOUDDESK_STEP:configure"=>"VPS · настройка systemd и Caddy",
        "CLOUDDESK_STEP:start"=>"VPS · запуск Orchestra",
        "CLOUDDESK_STEP:local-health"=>"VPS · проверка локальной идентичности",
        "CLOUDDESK_STEP:https"=>"VPS · проверка HTTPS",
        "CLOUDDESK_STEP:verify-storage"=>"VPS · ожидание готовности хранилища",
        "CLOUDDESK_STEP:maintenance"=>"VPS · восстановление службы обслуживания",
        "CLOUDDESK_STEP:complete"=>"VPS · подтверждение установки",
        "CLOUDDESK_RESET_STEP:stop"=>"VPS · остановка служб перед подтверждённым сбросом",
        "CLOUDDESK_RESET_STEP:delete"=>"VPS · удаление подтверждённых данных",
        "CLOUDDESK_RESET_STEP:install"=>"VPS · установка нового узла",
        "CLOUDDESK_RESET_STEP:verify-identity"=>"VPS · подтверждение новой идентичности",
        "CLOUDDESK_RESET_STEP:verify-storage"=>"VPS · проверка новых файлов SQLite",
        "CLOUDDESK_RESET_STEP:complete"=>"VPS · сброс подтверждён",
        "CLOUDDESK_MAINTENANCE_PENDING"=>"VPS · установка завершена, требуется безопасное восстановление обслуживания",
        "CLOUDDESK_MAINTENANCE_SCHEDULED"=>"VPS · компоненты проверяются локальной службой",
        "CLOUDDESK_RESET_MAINTENANCE_PENDING"=>"VPS · новый узел установлен, требуется ремонт службы обслуживания",
        "CLOUDDESK_RESET_MAINTENANCE_SCHEDULED"=>"VPS · серверное обслуживание запланировано",
        _=>null
    };
    static Guid? InstallIdentity(string output,string marker)
    {
        var matches=Regex.Matches(output,@"(?m)^"+Regex.Escape(marker)+@" ([a-fA-F0-9-]{36})\r?$");
        return matches.Count==1&&Guid.TryParse(matches[0].Groups[1].Value,out var node)&&node!=Guid.Empty?node:null;
    }
    async Task<object> ExecuteFactoryReset(JsonObject args,Action<string> progress)
    {
        var approval=resetApproval;
        resetApproval=null;
        if(approval==null||approval.Expires<=DateTimeOffset.UtcNow||approval.Nonce!=S(args,"nonce")||S(args,"confirm")!="RESET_ORCHESTRA"||
           !ResetConfirmationMatches(approval.Domain,S(args,"confirmation"))||approval.Domain!=S(args,"domain")||approval.Node!=S(args,"node")||
           approval.Host!=S(args,"host")||approval.ServerId!=Num(args,"serverId")||approval.Release.Tag!=S(args,"releaseTag"))
            throw new Exception("Подтверждение полного сброса отсутствует, устарело или относится к другому VPS.");
        var identity=await ResetIdentity(args);
        var release=await LatestResetRelease();
        if(identity.Domain!=approval.Domain||identity.Node!=approval.Node||release!=approval.Release)
            throw new Exception("Цель или релиз изменились. Повторите проверку и подтверждение.");
        var temp=Directory.CreateTempSubdirectory("mysticego-reset-").FullName;
        var remote="/root/.clouddesk-reset-"+Guid.NewGuid().ToString("N");
        try {
            progress("Скачивание актуального релиза до удаления данных");
            await Exec("gh",["release","download",release.Tag,"--repo",Repo,"--pattern",release.Asset,"--dir",temp]);
            var archive=Path.Combine(temp,release.Asset);
            using(var fs=File.OpenRead(archive))
                if(Convert.ToHexString(await SHA256.HashDataAsync(fs)).ToLowerInvariant()!=release.Digest)throw new Exception("SHA-256 архива не совпадает.");
            var script=ResetScriptPath();var installer=ResetInstallerPath();
            var scriptDigest=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(script))).ToLowerInvariant();
            var installerDigest=Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(installer))).ToLowerInvariant();
            progress("Передача проверенного архива и профилей по SSH");
            await SSH(args,"umask 077; mkdir -m 700 "+remote,1);
            await Upload(args,archive,remote+"/r3.zip");
            await Upload(args,script,remote+"/reset.sh");
            await Upload(args,installer,remote+"/install.sh");
            // Recheck server identity and owner once more after a potentially long transfer.
            await ResetIdentity(args);
            var command="cd "+remote+" && printf '%s  reset.sh\\n%s  install.sh\\n' "+scriptDigest+" "+installerDigest+
                " | sha256sum -c - && bash reset.sh execute "+ResetQ(identity.Domain)+" "+ResetQ(identity.Node)+" "+ResetQ(release.Tag)+" "+ResetQ(release.Digest);
            progress("Проверка цели на VPS перед необратимым сбросом");
            var installStarted=false;
            var reconnectVerified=false;
            string output;
            try
            {
                output=await SSH(args,command,45,line=>{
                    if(line.Trim()=="CLOUDDESK_RESET_STEP:install")installStarted=true;
                    var message=DeploymentStepMessage(line);if(message!=null)progress(message);
                });
            }
            catch(Exception original) when(installStarted && original is not OperationCanceledException)
            {
                // Verify only the exact, previously uploaded script on the same trusted VPS.
                // Never call execute a second time.
                progress("Проверяем результат установки без повторного удаления");
                try
                {
                    await VerifyReuploadTarget(args);
                    output=await SSH(args,"cd "+remote+" && printf '%s  reset.sh\\n' "+scriptDigest+
                        " | sha256sum -c - && bash reset.sh verify "+ResetQ(identity.Domain)+" "+ResetQ(identity.Node)+" "+ResetQ(release.Tag),4,
                        line=>{var message=DeploymentStepMessage(line);if(message!=null)progress(message);});
                    reconnectVerified=true;
                }
                catch(Exception verification)
                {
                    throw new Exception("Итог переустановки не подтверждён. Повторный сброс не выполнялся.\nПроверка: "+verification.Message+"\nПервоначальный результат: "+original.Message);
                }
            }
            var verified=InstallIdentity(output,"FACTORY_RESET_OK");
            if(verified is not Guid newNode||newNode.ToString("D")==identity.Node||(!reconnectVerified&&InstallIdentity(output,"ORCHESTRA_INSTALL_OK")!=newNode))
                throw new Exception("Новая Orchestra не подтвердила новый ID. Не повторяйте сброс автоматически.");
            var binding=ReadBinding(approval.ServerId);
            if(binding!=null&&SameBinding(binding,approval.Host,"Orchestra",approval.Domain))SaveBinding(binding with {Phase="installed"});
            try{await SSH(args,"rm -rf -- "+remote,1);}catch{}
            var maintenancePending=output.Split('\n').Any(line=>line.Trim()=="CLOUDDESK_RESET_MAINTENANCE_PENDING"||line.Trim()=="CLOUDDESK_MAINTENANCE_PENDING");
            var maintenanceScheduled=!maintenancePending&&output.Split('\n').Any(line=>line.Trim()=="CLOUDDESK_RESET_MAINTENANCE_SCHEDULED");
            progress(maintenancePending?"Новый узел подтверждён; требуется восстановление обслуживания без сброса.":"Новый узел подтверждён; компоненты проверяются серверной службой.");
            return new {role="Orchestra",domain=identity.Domain,release=release.Tag,node=newNode.ToString("D"),reset=true,maintenancePending,maintenanceScheduled};
        }catch(Exception e){throw new Exception(e.Message+"\nПовторный сброс автоматически не выполняется. Если staging создан, он находится в "+remote);}
        finally{Directory.Delete(temp,true);}
    }
    static object FactoryResetSelftest()
    {
        var rows=JsonNode.Parse("""[{"tagName":"v1.0.0-beta.7-r3.2.9","isDraft":false,"publishedAt":"2026-01-01T00:00:00Z"},{"tagName":"v1.0.0-beta.7-r3.2.40","isDraft":false,"publishedAt":"2026-01-01T00:00:00Z"},{"tagName":"v1.0.0-beta.7-r3.2.41","isDraft":true,"publishedAt":null}]""")!.AsArray();
        if(ResetNewestTag(rows)!="v1.0.0-beta.7-r3.2.40"||ResetAsset(ResetNewestTag(rows))!="MysticEgo_BETA7_R3.2.40.zip"||
           ResetVersion("../main")!=null||!ResetConfirmationMatches("orchestra.example.org","DELETE orchestra.example.org")||
           ResetConfirmationMatches("orchestra.example.org","DELETE other.example.org"))
            throw new Exception("Factory reset policy regression.");
        var rejected=false;try{ResetReleaseFromMetadata(new JsonObject{["tag_name"]=ResetNewestTag(rows),["draft"]=true},ResetNewestTag(rows));}catch{rejected=true;}
        if(!rejected)throw new Exception("Draft accepted for factory reset.");
        var node=Guid.Parse("12345678-1234-4234-9234-123456789abc");
        var marker="ORCHESTRA_INSTALL_OK "+node.ToString("D");
        if(InstallIdentity(marker+"\r\n","ORCHESTRA_INSTALL_OK")!=node||
            InstallIdentity(marker+"\n"+marker,"ORCHESTRA_INSTALL_OK")!=null||
            InstallIdentity("ORCHESTRA_INSTALL_OK "+Guid.Empty.ToString("D"),"ORCHESTRA_INSTALL_OK")!=null||
            DeploymentStepMessage("CLOUDDESK_RESET_STEP:verify-storage")==null||
            DeploymentStepMessage("unstructured output")!=null)
            throw new Exception("Installation evidence or progress mapping regression.");
        return new {passed=11};
    }
}
