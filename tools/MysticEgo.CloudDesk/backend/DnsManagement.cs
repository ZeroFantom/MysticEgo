using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Net;

sealed partial class CloudDesk {
    // All inventory and mutation operations use the account's real DNS record IDs.
    sealed record DnsRow(string Id,string Name,string Type,string Value,string Data,string RecordZone,string InventoryZone="");
    async Task<JsonObject> OwnedZone(string root) => (await DomainRows()).FirstOrDefault(d=>S(d,"fqdn").Equals(root,StringComparison.OrdinalIgnoreCase))??throw new Exception("Домен не найден в аккаунте Timeweb.");
    async Task<List<DnsRow>> ReadDnsInventory(string root) {
        var result=await Api("domains/"+Uri.EscapeDataString(root)+"/dns-records",version:2);
        if(result["dns_records"] is not JsonArray records)throw new Exception("Неизвестный формат DNS Timeweb.");
        var rows=new List<DnsRow>();
        foreach(var item in records){
            if(item is not JsonObject r)throw new Exception("Неизвестный формат DNS-записи.");
            if(r["data"] is not JsonObject data||S(r,"fqdn")=="")throw new Exception("Неизвестный формат DNS-записи.");
            var baseName=S(r,"fqdn").TrimEnd('.').ToLowerInvariant();var sub=S(data,"subdomain");
            var name=(sub.Length==0||sub=="@"?baseName:sub+"."+baseName).TrimEnd('.').ToLowerInvariant();
            if(name!=root&&!name.EndsWith("."+root,StringComparison.OrdinalIgnoreCase))throw new Exception("Ответ DNS содержит запись вне выбранной зоны.");
            rows.Add(new(S(r,"id"),name,S(r,"type").ToUpperInvariant(),S(data,"value"),data.ToJsonString(),baseName,root));
        }
        return rows;
    }
    static string AutoLabel(string role,int n)=>role=="Orchestra"?(n==0?"orchestra":$"orchestra-{n:D3}"):$"global-{n:D3}";
    async Task<object> SuggestDns(JsonObject a){
        var root=Need(a,"zone",DnsNamePattern).ToLowerInvariant();var role=Need(a,"role",@"^(Orchestra|Global)$");
        var z=await OwnedZone(root);if(!DomainUsable(z))throw new Exception("Домен не активен.");
        var rows=await ReadDnsInventory(root);var subs=(z["subdomains"] as JsonArray??throw new Exception("Нет списка поддоменов.")).OfType<JsonObject>().Select(s=>S(s,"fqdn").ToLowerInvariant()).ToHashSet();
        var host=S(a,"host");var dual=S(a,"network")=="dual";var v6="";
        if(host!=""){
            var id=Num(a,"serverId");var ips=await PublicIps(id);
            if(!PublicAddress(host,"ipv4")||!ips.OfType<JsonObject>().Any(i=>S(i,"type")=="ipv4"&&SameIp(S(i,"ip"),host)))throw new Exception("IP не подтверждён для выбранного VPS.");
            if(dual)v6=FindPublic(ips,"ipv6");
            var b=ReadBinding(id);var aliases=await ProjectAliases(host,ips);
            if(b!=null&&(!SameIp(b.Host,host)||b.Role!=role))throw new Exception($"VPS уже закреплён за {b.Role} / {b.Domain}.");
            
            var names=aliases.Select(r=>r.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            if(b!=null||names.Length>0){
                var matching=names.Where(n=>DeploymentRole(n)==role).ToArray();
                if(b==null&&matching.Length==0)throw new Exception("VPS уже имеет DNS другой роли: "+string.Join(", ",names));
                var chosen=b?.Domain??matching[0];var requested=S(a,"preferred");
                if(b==null&&matching.Contains(requested,StringComparer.OrdinalIgnoreCase))chosen=requested;
                if(!chosen.EndsWith("."+root,StringComparison.OrdinalIgnoreCase))throw new Exception("VPS уже имеет DNS в другой зоне: "+chosen+". Выберите эту зону.");
                return new {domain=chosen,label=chosen[..^(root.Length+1)],reused=true,aliases=b==null?matching:new[]{chosen},duplicates=names.Where(n=>n!=chosen).ToArray(),deleteRecords=aliases.Where(r=>r.Name!=chosen).Select(DnsPreview).ToArray(),installed=b?.Phase=="installed"};
            }
        }
        if(host==""&&role=="Orchestra"){
            foreach(var zone in (await DomainRows()).Where(DomainUsable)){
                var records=await ReadDnsInventory(S(zone,"fqdn").ToLowerInvariant());
                if(records.Any(r=>DeploymentRole(r.Name)=="Orchestra"&&(r.Type is "A" or "AAAA")))throw new Exception("Уже существует DNS Orchestra. Выберите существующий VPS и продолжите развёртывание, не создавая новый сервер.");
            }
        }
        string? free=null;
        for(var n=role=="Orchestra"?0:1;n<1000;n++){
            var label=AutoLabel(role,n);var fqdn=label+"."+root;
            if(rows.Any(r=>r.Name.StartsWith("*.")&&fqdn.EndsWith(r.Name[1..],StringComparison.OrdinalIgnoreCase)))throw new Exception("Wildcard DNS: автоматический выбор имени запрещён для этой зоны.");
            var existing=rows.Where(r=>r.Name==fqdn).ToArray();
            if(!subs.Contains(fqdn)&&existing.Length==0){free??=label;if(host=="")break;continue;}
            if(host!=""&&subs.Contains(fqdn)&&existing.Any(r=>r.Type=="A"&&SameIp(r.Value,host))&&existing.All(r=>r.Type=="A"&&SameIp(r.Value,host)||r.Type=="AAAA"&&dual&&v6!=""&&SameIp(r.Value,v6)))return new {domain=fqdn,label,reused=true};
        }
        if(free!=null)return new {domain=free+"."+root,label=free,reused=false};
        throw new Exception("Нет свободных имён в выбранной зоне.");
    }
    // Protect project-looking names even when the central registry is temporarily unreachable.
    static bool ProjectDnsName(string name)=>name.Split('.').Any(s=>Regex.IsMatch(s,@"^(?:mysticego-)?(?:orchestra|global|local|agent|audit)(?:-[a-z0-9-]+)?$",RegexOptions.IgnoreCase));
    readonly object protectedDnsLock=new();
    readonly HashSet<string> protectedDnsNames=new(StringComparer.OrdinalIgnoreCase);
    void ProtectOrigin(string origin){if(Uri.TryCreate(origin,UriKind.Absolute,out var u)&&Regex.IsMatch(u.Host,DnsNamePattern))lock(protectedDnsLock)protectedDnsNames.Add(u.Host);}
    bool ProtectedDns(DnsRow r,string root){
        if(r.Name==root||r.Name.StartsWith("*.")||ProjectDnsName(r.Name)||r.Type is not ("A" or "AAAA" or "CNAME"))return true;
        lock(protectedDnsLock)return protectedDnsNames.Contains(r.Name);
    }
    async Task<object> DnsInventory(JsonObject a){
        var root=Need(a,"zone",DnsNamePattern).ToLowerInvariant();await OwnedZone(root);
        var rows=await ReadDnsInventory(root);
        return new{rows=rows.Select(r=>new{id=r.Id,name=r.Name,type=r.Type,value=r.Value,data=(r.Type is "A" or "AAAA" or "CNAME")?r.Data:"",blocked=ProtectedDns(r,root)||!Regex.IsMatch(r.Id,@"^[1-9][0-9]*$")}).ToArray()};
    }
    async Task<object> DeleteDns(JsonObject a){
        if(S(a,"confirm")!="DELETE_DNS")throw new Exception("Не подтверждено удаление DNS.");
        var root=Need(a,"zone",DnsNamePattern).ToLowerInvariant();await OwnedZone(root);
        var selected=a["records"] as JsonArray??throw new Exception("Нет выбранных записей.");
        if(selected.Count is <1 or >100)throw new Exception("Выберите от 1 до 100 записей.");
        var current=await ReadDnsInventory(root);var targets=new List<DnsRow>();
        foreach(var item in selected){var x=item?.AsObject()??throw new Exception("Неверная запись.");var id=Need(x,"id",@"^[1-9][0-9]*$");
            var r=current.SingleOrDefault(r=>r.Id==id)??throw new Exception("Запись изменилась. Дождитесь синхронизации.");
            if(ProtectedDns(r,root)||r.Name!=S(x,"name")||r.Type!=S(x,"type")||r.Value!=S(x,"value")||r.Data!=(x["data"]?.GetValue<string>()??"")||targets.Any(t=>t.Id==id))throw new Exception("Запись защищена или изменилась. Ничего не удалено.");
            targets.Add(r);
        }
        var deleted=new List<string>();string error="";
        foreach(var r in targets){try{await DeleteExactDns(r);deleted.Add(r.Id);}catch(Exception e){error=e.Message;break;}}
        return new{deleted,error,requested=targets.Count};
    }
    static object NamingSelftest(){if(AutoLabel("Orchestra",0)!="orchestra"||AutoLabel("Global",1)!="global-001"||!ProjectDnsName("global-002.example.org")||ProjectDnsName("mail.example.org"))throw new Exception("DNS naming selftest failed");return new{passed=4};}
}
