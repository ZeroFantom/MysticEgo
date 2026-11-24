using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed partial class CloudDesk {
    static bool MissingDnsRecord(TimewebApiException e) => e.Status==404 && e.ErrorCode=="dns_record_not_found" && e.Method=="DELETE" && e.Version==2;
    static bool MissingDnsScope(TimewebApiException e) => e.Status==404 && (e.ErrorCode is "domain_not_found" or "subdomain_not_found") && e.Method=="GET" && e.Version==2;
    // A zone listing and a subdomain listing can represent the same record using
    // different fqdn/subdomain fields. All other record data must still match.
    static JsonObject ComparableDnsData(DnsRow r) {
        var data=JsonNode.Parse(r.Data)?.AsObject()??throw new Exception("Некорректные данные DNS.");
        data.Remove("subdomain");return data;
    }
    static bool SameDnsIdentity(DnsRow a,DnsRow b) => a.Id==b.Id && a.Name==b.Name && a.Type==b.Type && a.Value==b.Value && JsonNode.DeepEquals(ComparableDnsData(a),ComparableDnsData(b));
    static string[] DnsDeleteScopes(DnsRow r) {
        var root=r.InventoryZone.Length==0?r.RecordZone:r.InventoryZone;
        if(!Regex.IsMatch(r.Id,@"^[1-9][0-9]*$")||!Regex.IsMatch(root,DnsNamePattern)||!Regex.IsMatch(r.Name,DnsNamePattern)||!Regex.IsMatch(r.RecordZone,DnsNamePattern)||
           !(r.Name==root||r.Name.EndsWith("."+root,StringComparison.OrdinalIgnoreCase))||
           !(r.RecordZone==root||r.RecordZone.EndsWith("."+root,StringComparison.OrdinalIgnoreCase))||
           !(r.Name==r.RecordZone||r.Name.EndsWith("."+r.RecordZone,StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Некорректная область удаления DNS.");
        return new[]{r.Name,r.RecordZone,root}.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
    async Task DeleteExactDns(DnsRow row) => await DeleteExactDnsCore(row,ReadDnsInventory,async (scope,id)=>{
        await Api("domains/"+Uri.EscapeDataString(scope)+"/dns-records/"+Uri.EscapeDataString(id),version:2,method:HttpMethod.Delete);
    });
    static async Task DeleteExactDnsCore(DnsRow row,Func<string,Task<List<DnsRow>>> read,Func<string,string,Task> remove) {
        var scopes=DnsDeleteScopes(row);
        var root=row.InventoryZone.Length==0?row.RecordZone:row.InventoryZone;
        async Task<List<string>> Locate(){
            var found=new List<string>();
            // Always verify the account inventory as well as the full subdomain.
            foreach(var scope in scopes){
                List<DnsRow> rows;
                try{rows=await read(scope);}
                catch(TimewebApiException e) when(scope!=root&&MissingDnsScope(e)){continue;}
                var sameId=rows.Where(r=>r.Id==row.Id).ToArray();
                if(sameId.Any(r=>!SameDnsIdentity(row,r)))throw new Exception("DNS-запись #"+row.Id+" изменилась. Удаление остановлено.");
                if(sameId.Length>0)found.Add(scope);
            }
            return found;
        }
        var current=await Locate();
        if(current.Count==0)return; // Absence is confirmed by fresh successful inventory.
        var tried=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while(current.FirstOrDefault(s=>!tried.Contains(s)) is string scope){
            tried.Add(scope);
            try{await remove(scope,row.Id);}
            catch(TimewebApiException e) when(MissingDnsRecord(e)){
                current=await Locate();if(current.Count==0)return;
                continue; // Only another independently verified scope, never another ID.
            }
            if((await Locate()).Count==0)return;
            throw new Exception("Timeweb принял удаление #"+row.Id+", но запись ещё есть в API. Дождитесь синхронизации; повторное удаление не отправлено.");
        }
        throw new Exception("Timeweb не нашёл DNS-запись #"+row.Id+" при удалении, хотя она ещё есть в каталоге. DNS не подтверждён; повторите после синхронизации.");
    }
    static async Task<object> DnsDeleteSelftest(){
        var root="example.org";var name="orchestra-001.example.org";
        var row=new DnsRow("10",name,"A","147.45.170.117","{\"value\":\"147.45.170.117\",\"subdomain\":\"orchestra-001\"}",root,root);
        var direct=row with {Data="{\"value\":\"147.45.170.117\"}",RecordZone=name,InventoryZone=name};
        var other=row with {Id="11",Type="TXT",Value="verification",Data="{\"value\":\"verification\"}"};
        int passed=0;
        void Check(bool ok){if(!ok)throw new Exception("DNS delete selftest failed");passed++;}
        TimewebApiException Missing()=>new("DELETE",2,"domains/example.org/dns-records/10",404,"dns_record_not_found","not found");
        Check(DnsDeleteScopes(row).SequenceEqual(new[]{name,root}));Check(SameDnsIdentity(row,direct));Check(!SameDnsIdentity(row,other));
        bool present=true;var calls=new List<string>();
        Task<List<DnsRow>> Read(string scope)=>Task.FromResult(present?(scope==name?new List<DnsRow>{direct}:new List<DnsRow>{row,other}):new List<DnsRow>{other});
        Task Remove(string scope,string id){calls.Add(scope+":"+id);present=false;return Task.CompletedTask;}
        await DeleteExactDnsCore(row,Read,Remove);Check(calls.SequenceEqual(new[]{name+":10"}));
        calls.Clear();await DeleteExactDnsCore(row,Read,Remove);Check(calls.Count==0);
        present=true;calls.Clear();await DeleteExactDnsCore(row,Read,(scope,id)=>{calls.Add(scope);present=false;throw Missing();});Check(calls.SequenceEqual(new[]{name}));
        present=true;calls.Clear();await DeleteExactDnsCore(row,Read,(scope,id)=>{calls.Add(scope);if(scope==name)throw Missing();present=false;return Task.CompletedTask;});Check(calls.SequenceEqual(new[]{name,root}));
        present=true;calls.Clear();bool blocked=false;try{await DeleteExactDnsCore(row,Read,(scope,id)=>{calls.Add(scope);throw Missing();});}catch(Exception){blocked=true;}Check(blocked&&calls.SequenceEqual(new[]{name,root}));
        calls.Clear();blocked=false;try{await DeleteExactDnsCore(row,_=>Task.FromResult(new List<DnsRow>{row with {Value="147.45.170.118"}}),Remove);}catch(Exception){blocked=true;}Check(blocked&&calls.Count==0);
        present=true;calls.Clear();blocked=false;try{await DeleteExactDnsCore(row,Read,(scope,id)=>{calls.Add(scope);throw new TimewebApiException("DELETE",2,"",404,"other_error","not found");});}catch(TimewebApiException){blocked=true;}Check(blocked&&calls.Count==1);
        present=true;calls.Clear();blocked=false;try{await DeleteExactDnsCore(row,Read,(scope,id)=>{calls.Add(scope);return Task.CompletedTask;});}catch(Exception){blocked=true;}Check(blocked&&calls.Count==1);
        return new{passed};
    }
}
