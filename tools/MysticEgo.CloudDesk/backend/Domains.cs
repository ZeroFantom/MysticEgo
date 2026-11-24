using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed partial class CloudDesk {
    const string DnsNamePattern = @"^(?=.{1,253}$)(?:[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,63}$";
    static bool DomainUsable(JsonObject d) => S(d,"domain_status") is "paid" or "soon_expire" or "ns_based" or "free";
    async Task<List<JsonObject>> DomainRows() {
        var rows=new List<JsonObject>();
        for(int offset=0;offset<10000;offset+=100) {
            var result=await Api($"domains?limit=100&offset={offset}");
            if(result["domains"] is not JsonArray page) throw new Exception("Неизвестный формат списка доменов Timeweb.");
            rows.AddRange(page.OfType<JsonObject>());
            if(page.Count<100) return rows;
        }
        throw new Exception("Слишком большой список доменов; автоматическая настройка отключена.");
    }
    async Task<object> Domains() {
        var rows=await DomainRows();
        return new {domains=rows.Where(d=>Regex.IsMatch(S(d,"fqdn"),DnsNamePattern)).Select(d=>new {fqdn=S(d,"fqdn").ToLowerInvariant(),status=S(d,"domain_status"),usable=DomainUsable(d),expiration=S(d,"expiration"),subdomains=(d["subdomains"] as JsonArray??new JsonArray()).OfType<JsonObject>().Select(x=>S(x,"fqdn")).Where(x=>Regex.IsMatch(x,DnsNamePattern)).ToArray()}).ToArray()};
    }
    async Task<(JsonObject zone,string fqdn,string label)> DomainTarget(JsonObject a) {
        var root=Need(a,"dnsZone",DnsNamePattern).ToLowerInvariant();
        var fqdn=Need(a,"domain",DnsNamePattern).ToLowerInvariant();
        if(!fqdn.EndsWith("."+root,StringComparison.Ordinal)) throw new Exception("Автоматический DNS доступен только для поддомена выбранной зоны.");
        var label=fqdn[..^(root.Length+1)];
        if(!Regex.IsMatch(label,@"^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")) throw new Exception("Укажите один уровень поддомена.");
        var zone=(await DomainRows()).FirstOrDefault(d=>S(d,"fqdn").Equals(root,StringComparison.OrdinalIgnoreCase)&&DomainUsable(d))??throw new Exception("Домен не найден в активных зонах Timeweb.");
        if(zone["subdomains"] is not JsonArray) throw new Exception("API не подтвердил список поддоменов; DNS не изменён.");
        return (zone,fqdn,label);
    }
    async Task<List<(string type,string value)>> DnsRecords(string zone,string fqdn) {
        var result=await Api("domains/"+Uri.EscapeDataString(zone)+"/dns-records",version:2);
        if(result["dns_records"] is not JsonArray records) throw new Exception("Неизвестный формат DNS Timeweb; записи не изменены.");
        var list=new List<(string type,string value)>();
        foreach(var r in records.OfType<JsonObject>()) {
            if(r["data"] is not JsonObject data || S(r,"fqdn")=="") throw new Exception("Неизвестный формат DNS-записи; изменения запрещены.");
            var sub=S(data,"subdomain");
            var name=(sub.Length==0||sub=="@"?S(r,"fqdn"):sub+"."+S(r,"fqdn")).TrimEnd('.');
            if(name.Equals(fqdn,StringComparison.OrdinalIgnoreCase)) list.Add((S(r,"type").ToUpperInvariant(),S(data,"value")));
            // A wildcard may already serve an existing application. Never shadow it silently.
            if(name.StartsWith("*.")&&fqdn.EndsWith(name[1..],StringComparison.OrdinalIgnoreCase)) throw new Exception("В зоне есть wildcard DNS. Выберите ручную настройку, чтобы не затронуть действующий сервис.");
        }
        return list;
    }
    static bool SameIp(string a,string b)=>IPAddress.TryParse(a,out var x)&&IPAddress.TryParse(b,out var y)&&x.Equals(y);
    static void CheckDnsConflict(List<(string type,string value)> rows,string ipv4,string ipv6) {
        foreach(var r in rows) {
            if((r.type is "CNAME" or "HTTPS" or "SVCB" or "NS") || r.type=="A"&&(ipv4==""||!SameIp(r.value,ipv4)) || r.type=="AAAA"&&(ipv6==""||!SameIp(r.value,ipv6))) throw new Exception("DNS-имя уже занято или ведёт на другой адрес. Выберите другой поддомен. Существующие записи не изменены.");
        }
    }
    static object DnsSelftest() {
        int passed=0;
        void Test(List<(string type,string value)> rows,string a,string aaaa,bool blocked) {
            bool rejected=false; try { CheckDnsConflict(rows,a,aaaa); } catch { rejected=true; }
            if(rejected!=blocked) throw new Exception("DNS conflict selftest failed"); passed++;
        }
        Test([],"","",false);
        Test([("A","8.8.8.8")],"8.8.8.8","",false);
        Test([("A","8.8.8.8")],"1.1.1.1","",true);
        Test([("A","8.8.8.8")],"","",true);
        Test([("CNAME","other.example.org")],"8.8.8.8","",true);
        Test([("AAAA","2001:db8::1")],"8.8.8.8","2001:db8:0:0:0:0:0:1",false);
        Test([("AAAA","2001:db8::1")],"8.8.8.8","",true);
        Test([("TXT","existing")],"8.8.8.8","",false);
        return new {passed};
    }
    async Task<object> DnsPrepare(JsonObject a,bool checkOnly) {
        if(S(a,"confirm")!="DEPLOY") throw new Exception("Подтвердите развёртывание и DNS.");
        var (zone,fqdn,label)=await DomainTarget(a); var root=S(zone,"fqdn");
        var exists=((JsonArray)zone["subdomains"]!).OfType<JsonObject>().Any(d=>S(d,"fqdn").Equals(fqdn,StringComparison.OrdinalIgnoreCase));
        string ipv4="",ipv6="";
        if(S(a,"serverId")!="") {
            var id=Num(a,"serverId"); ipv4=Need(a,"host",@"^[0-9.]+$");
            var ips=await PublicIps(id);
            if(!PublicAddress(ipv4,"ipv4")||!ips.OfType<JsonObject>().Any(x=>S(x,"type")=="ipv4"&&SameIp(S(x,"ip"),ipv4))) throw new Exception("IPv4 не подтверждён для выбранного VPS.");
            if(S(a,"network")=="dual") {ipv6=FindPublic(ips,"ipv6"); if(ipv6=="") throw new Exception("У выбранного VPS нет публичного IPv6. Выберите IPv4 либо VPS с IPv6.");}
        } else if(!checkOnly) throw new Exception("DNS можно привязать только к существующему VPS Timeweb.");
        if(S(a,"serverId")!="") {
            if(checkOnly){var t=await BindingTarget(a);var b=ReadBinding(t.id);if(b!=null&&!SameBinding(b,t.host,t.role,t.domain)||t.aliases.Any(r=>r.Name!=t.domain||DeploymentRole(r.Name)!=t.role))throw new Exception("VPS уже имеет другую роль или DNS-имя. Устраните дубли перед развёртыванием.");}
            else await ReserveBinding(a);
        }
        var rows=await DnsRecords(root,fqdn); CheckDnsConflict(rows,ipv4,ipv6);
        if(!exists&&rows.Count>0) throw new Exception("Имя уже имеет DNS-записи, но не подтверждено как отдельный поддомен. Выберите другое имя или ручную настройку.");
        if(checkOnly) {if(ipv4==""&&exists) throw new Exception("Поддомен уже создан. Для нового VPS выберите новое DNS-имя."); return new {ready=true};}
        if(!exists) {
            await Api($"domains/{Uri.EscapeDataString(root)}/subdomains/{Uri.EscapeDataString(label)}",method:HttpMethod.Post);
            rows=await DnsRecords(root,fqdn); CheckDnsConflict(rows,ipv4,ipv6);
        }
        if(!rows.Any(r=>r.type=="A"&&SameIp(r.value,ipv4))) await Api($"domains/{Uri.EscapeDataString(fqdn)}/dns-records",new {type="A",value=ipv4},version:2);
        if(ipv6!=""&&!rows.Any(r=>r.type=="AAAA"&&SameIp(r.value,ipv6))) await Api($"domains/{Uri.EscapeDataString(fqdn)}/dns-records",new {type="AAAA",value=ipv6});
        var verified=await DnsRecords(root,fqdn); CheckDnsConflict(verified,ipv4,ipv6);
        if(!verified.Any(r=>r.type=="A"&&SameIp(r.value,ipv4)) || ipv6!=""&&!verified.Any(r=>r.type=="AAAA"&&SameIp(r.value,ipv6))) throw new Exception("Записи ещё не подтверждены API. Проверьте DNS перед продолжением; повторный заказ VPS не требуется.");
        return new {ready=true,domain=fqdn,ipv4,ipv6};
    }
}
