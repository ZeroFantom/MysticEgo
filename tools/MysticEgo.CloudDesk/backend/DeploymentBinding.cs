using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net;

sealed partial class CloudDesk {
    // Durable identity of the deployment, not a topology node. Never infer a live node from it.
    sealed record Binding(int Id,string Host,string Role,string Domain,string Phase);
    string BindingPath(int id)=>Path.Combine(State,$"deploy-{id}.json");
    Binding? ReadBinding(int id) {
        var path=BindingPath(id); if(!File.Exists(path))return null;
        if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new Exception("Небезопасный файл привязки VPS.");
        return System.Text.Json.JsonSerializer.Deserialize<Binding>(File.ReadAllText(path))??throw new Exception("Не удалось прочитать привязку VPS.");
    }
    void SaveBinding(Binding b) {
        Init(); var path=BindingPath(b.Id); var tmp=path+"."+Guid.NewGuid().ToString("N");
        try{File.WriteAllText(tmp,System.Text.Json.JsonSerializer.Serialize(b));SetPrivateMode(tmp,Private);File.Move(tmp,path,true);}finally{if(File.Exists(tmp))File.Delete(tmp);}
    }
    static string DeploymentRole(string name) {
        var label=name.Split('.')[0];
        return Regex.IsMatch(label,@"^(?:mysticego-)?orchestra(?:-[0-9]+)?$",RegexOptions.IgnoreCase)?"Orchestra":Regex.IsMatch(label,@"^(?:mysticego-)?global(?:-[0-9]+)?$",RegexOptions.IgnoreCase)?"Global":"";
    }
    static bool SameBinding(Binding b,string host,string role,string domain)=>SameIp(b.Host,host)&&b.Role==role&&b.Domain.Equals(domain,StringComparison.OrdinalIgnoreCase);
    async Task<List<DnsRow>> ProjectAliases(string host,JsonArray ips) {
        var addresses=ips.OfType<JsonObject>().Select(x=>S(x,"ip")).Where(ip=>PublicAddress(ip,"ipv4")||PublicAddress(ip,"ipv6")).ToArray();
        if(!addresses.Any(ip=>SameIp(ip,host)))throw new Exception("IP не принадлежит выбранному VPS.");
        var rows=new List<DnsRow>();
        foreach(var zone in (await DomainRows()).Where(DomainUsable)) {
            var root=S(zone,"fqdn").ToLowerInvariant(); if(!Regex.IsMatch(root,DnsNamePattern))throw new Exception("Некорректная зона DNS.");
            rows.AddRange((await ReadDnsInventory(root)).Where(r=>DeploymentRole(r.Name)!=""&&(r.Type is "A" or "AAAA")&&addresses.Any(ip=>SameIp(ip,r.Value))));
        }
        return rows.DistinctBy(r=>(r.RecordZone,r.Id)).ToList();
    }
    async Task<(int id,string host,string role,string domain,JsonArray ips,List<DnsRow> aliases)> BindingTarget(JsonObject a) {
        var id=Num(a,"serverId");var host=Need(a,"host",@"^[0-9.]+$");var role=Need(a,"role",@"^(Orchestra|Global)$");var domain=Need(a,"domain",DnsNamePattern).ToLowerInvariant();
        var ips=await PublicIps(id); if(!PublicAddress(host,"ipv4")||!ips.OfType<JsonObject>().Any(x=>S(x,"type")=="ipv4"&&SameIp(S(x,"ip"),host)))throw new Exception("IPv4 не подтверждён для VPS.");
        return (id,host,role,domain,ips,await ProjectAliases(host,ips));
    }
    async Task<Binding> ReserveBinding(JsonObject a) {
        if(S(a,"confirm")!="DEPLOY")throw new Exception("Подтвердите развёртывание.");
        var t=await BindingTarget(a);var b=ReadBinding(t.id);
        if(b!=null&&!SameBinding(b,t.host,t.role,t.domain))throw new Exception($"VPS #{t.id} уже закреплён за {b.Role} / {b.Domain}. Новое имя или роль запрещены.");
        if(b?.Phase=="installed")throw new Exception("На VPS уже завершено развёртывание. Повторная установка запрещена.");
        var other=t.aliases.Select(r=>r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Where(n=>!n.Equals(t.domain,StringComparison.OrdinalIgnoreCase)).ToArray();
        if(other.Length>0)throw new Exception("На VPS уже есть проектные DNS-имена: "+string.Join(", ",other)+". Сначала устраните дубли; новая запись не создана.");
        if(t.aliases.Any(r=>DeploymentRole(r.Name)!=t.role))throw new Exception("На VPS закреплена другая роль.");
        if(b==null){
            var remote=await InspectDeployment(a);
            if(remote!="EMPTY")throw new Exception("На VPS уже обнаружена установка или конфигурация MysticEgo. Новое развёртывание запрещено: "+remote);
            b=new(t.id,t.host,t.role,t.domain,"reserved");SaveBinding(b);
        }
        return b;
    }
    Task<string> InspectDeployment(JsonObject a)=>SSH(a,"if test -f /etc/mysticego/orchestra.env; then sed -n 's|^MYSTICEGO_CENTER_ORCHESTRA_URL=https://||p' /etc/mysticego/orchestra.env; elif test -e /opt/mysticego/app || test -e /var/lib/mysticego || test -e /etc/systemd/system/mysticego-orchestra.service || test -e /etc/caddy/Caddyfile || systemctl list-unit-files --no-legend | grep -qi mysticego; then echo UNKNOWN; else echo EMPTY; fi",1);
    // A preview authorizes individual records, never an entire DNS name or zone.
    static object DnsPreview(DnsRow r)=>new {id=r.Id,zone=r.RecordZone,name=r.Name,type=r.Type,value=r.Value};
    static bool SameDnsRow(DnsRow a,DnsRow b)=>a.Id==b.Id&&a.RecordZone==b.RecordZone&&a.Name==b.Name&&a.Type==b.Type&&a.Value==b.Value&&JsonNode.DeepEquals(JsonNode.Parse(a.Data),JsonNode.Parse(b.Data));
    static bool PreviewMatches(JsonArray? preview,DnsRow[] targets){
        if(preview==null||preview.Count!=targets.Length)return false;
        var remaining=targets.ToList();
        foreach(var entry in preview){
            if(entry is not JsonObject x)return false;
            var i=remaining.FindIndex(r=>r.Id==S(x,"id")&&r.RecordZone==S(x,"zone")&&r.Name==S(x,"name")&&r.Type==S(x,"type")&&r.Value==S(x,"value"));
            if(i<0)return false;
            remaining.RemoveAt(i);
        }
        return remaining.Count==0;
    }
    // Only exact, explicitly selected aliases on this VPS. No zone/subdomain deletion.
    async Task<object> ReconcileDns(JsonObject a) {
        if(S(a,"confirm")!="REMOVE_ALIAS")throw new Exception("Подтвердите удаление дублирующих записей.");
        var t=await BindingTarget(a); var b=ReadBinding(t.id);
        if(b!=null&&!SameBinding(b,t.host,t.role,t.domain))throw new Exception("Выбранное имя не совпадает с сохранённой привязкой.");
        if(!t.aliases.Any(r=>r.Name==t.domain&&r.Type=="A"&&SameIp(r.Value,t.host)))throw new Exception("Сохраняемое имя не имеет подтверждённой A-записи.");
        if(DeploymentRole(t.domain)!=t.role)throw new Exception("Сохраняемое имя не соответствует выбранной роли.");
        // Trusted SSH only; don't remove the DNS of an existing installation or an unknown custom service.
        var installed=await InspectDeployment(a);
        if(installed!="EMPTY"&&!installed.Equals(t.domain,StringComparison.OrdinalIgnoreCase))throw new Exception("Установленная роль или домен не совпадают с выбранным именем. DNS не изменён: "+installed);
        var duplicates=t.aliases.Where(r=>r.Name!=t.domain).ToArray();
        var selected=(a["aliases"] as JsonArray??new()).Select(x=>x?.ToString()??"").ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(!selected.SetEquals(duplicates.Select(r=>r.Name)))throw new Exception("Список дублей изменился. Дождитесь повторной проверки.");
        foreach(var r in duplicates){
            lock(protectedDnsLock)if(protectedDnsNames.Contains(r.Name))throw new Exception("Имя подтверждено работающим реестром; удаление запрещено: "+r.Name);
            if(!Regex.IsMatch(r.Id,@"^[1-9][0-9]*$"))throw new Exception("Некорректный ID DNS-записи.");
        }
        if(duplicates.Length==0||!PreviewMatches(a["records"] as JsonArray,duplicates))throw new Exception("Состав A/AAAA-записей изменился. Дождитесь синхронизации и снова подтвердите очистку.");
        // Other DNS records (including foreign A/AAAA, TXT, MX, NS) are not targets.
        // Re-read all relevant zones before mutation; compare exact record IDs and data.
        var roots=(await DomainRows()).Where(DomainUsable).Select(z=>S(z,"fqdn").ToLowerInvariant()).Distinct().ToArray();
        if(roots.Any(selected.Contains))throw new Exception("Корневая запись зоны защищена.");
        var current=new List<DnsRow>();
        foreach(var root in roots.Where(root=>selected.Any(n=>n.EndsWith("."+root,StringComparison.OrdinalIgnoreCase))||t.domain.EndsWith("."+root,StringComparison.OrdinalIgnoreCase)))
            current.AddRange(await ReadDnsInventory(root));
        current=current.DistinctBy(r=>(r.RecordZone,r.Id)).ToList();
        if(!current.Any(r=>r.Name==t.domain&&r.Type=="A"&&SameIp(r.Value,t.host)))throw new Exception("Основная A-запись изменилась. DNS не изменён.");
        var addresses=t.ips.OfType<JsonObject>().Select(x=>S(x,"ip")).Where(ip=>PublicAddress(ip,"ipv4")||PublicAddress(ip,"ipv6")).ToArray();
        var currentTargets=current.Where(r=>selected.Contains(r.Name)&&(r.Type is "A" or "AAAA")&&addresses.Any(ip=>SameIp(ip,r.Value))).ToArray();
        if(currentTargets.Length!=duplicates.Length||duplicates.Any(d=>!currentTargets.Any(r=>SameDnsRow(d,r))))throw new Exception("Записи изменились. Дождитесь синхронизации.");
        var preserved=current.Where(r=>selected.Contains(r.Name)&&!duplicates.Any(d=>SameDnsRow(d,r))).Select(r=>new{name=r.Name,type=r.Type}).Distinct().ToArray();
        var removed=new List<string>();
        foreach(var r in duplicates){
            lock(protectedDnsLock)if(protectedDnsNames.Contains(r.Name))throw new Exception("Имя защищено реестром: "+r.Name);
            await DeleteExactDns(r);
            removed.Add(r.Id);
        }
        var finalAliases=await ProjectAliases(t.host,t.ips);
        if(finalAliases.Any(r=>r.Name!=t.domain)||!finalAliases.Any(r=>r.Name==t.domain&&r.Type=="A"&&SameIp(r.Value,t.host)))throw new Exception("Очистка DNS ещё не подтверждена API или основная A-запись изменилась. Повторно проверьте DNS.");
        SaveBinding(b??new(t.id,t.host,t.role,t.domain,installed=="EMPTY"?"reserved":"installed"));return new {domain=t.domain,deleted=removed,preserved};
    }
    static object BindingSelftest(){
        var b=new Binding(1,"147.45.170.117","Orchestra","orchestra.example.org","reserved");
        if(!SameBinding(b,b.Host,b.Role,b.Domain)||SameBinding(b,b.Host,"Global",b.Domain)||SameBinding(b,b.Host,b.Role,"orchestra-001.example.org")||SameBinding(b,"147.45.170.118",b.Role,b.Domain)||DeploymentRole("orchestra-001.example.org")!="Orchestra"||DeploymentRole("global-001.example.org")!="Global"||DeploymentRole("mail.example.org")!="")throw new Exception("Binding regression failed");
        var a=new DnsRow("10","orchestra-001.example.org","A",b.Host,"{\"value\":\"147.45.170.117\"}","example.org");
        var txt=new DnsRow("11",a.Name,"TXT","verification","{\"value\":\"verification\"}",a.RecordZone);
        var foreign=a with {Id="12",Value="147.45.170.118"};
        var preview=System.Text.Json.JsonSerializer.SerializeToNode(new[]{DnsPreview(a)}) as JsonArray;
        if(!PreviewMatches(preview,[a])||PreviewMatches(preview,[txt])||PreviewMatches(preview,[foreign])||PreviewMatches(preview,[a,foreign])||SameDnsRow(a,txt)||SameDnsRow(a,foreign)||!SameDnsRow(a,a with {}))throw new Exception("DNS exact-record regression failed");
        return new {passed=14};
    }
}
