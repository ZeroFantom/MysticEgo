using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed partial class CloudDesk {
    static readonly HttpClient discoverHttp=new(new HttpClientHandler{AllowAutoRedirect=false,UseCookies=false}){Timeout=TimeSpan.FromSeconds(5)};
    // Discovery is read-only and anonymous. No account token is sent to candidate servers.
    async Task<object> DiscoverTopology(JsonObject a) {
        var zones=await DomainRows();
        var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var z in zones.Where(DomainUsable)){
            var fqdn=S(z,"fqdn");if(!Regex.IsMatch(fqdn,DnsNamePattern))continue;
            names.Add(fqdn);
            foreach(var sub in (z["subdomains"] as JsonArray??new()).OfType<JsonObject>()){
                var name=S(sub,"fqdn");if(Regex.IsMatch(name,DnsNamePattern)&&name.EndsWith("."+fqdn,StringComparison.OrdinalIgnoreCase))names.Add(name);
            }
        }
        // DNS-only names may not be listed as separately created subdomains.
        var activeZones=zones.Where(DomainUsable).Select(z=>S(z,"fqdn")).Where(n=>Regex.IsMatch(n,DnsNamePattern)).Take(20).ToArray();
        var recordNames=new System.Collections.Concurrent.ConcurrentBag<string>();int failedZones=0;
        await Parallel.ForEachAsync(activeZones,new ParallelOptions{MaxDegreeOfParallelism=4},async (zone,ct)=>{
            try{foreach(var r in await ReadDnsInventory(zone))if((r.Type is "A" or "AAAA" or "CNAME")&&Regex.IsMatch(r.Name,DnsNamePattern))recordNames.Add(r.Name);}
            catch{Interlocked.Increment(ref failedZones);}
        });
        foreach(var n in recordNames)names.Add(n);
        var cloud=await Api("servers?limit=100");
        foreach(var s in (cloud["servers"] as JsonArray??new()).OfType<JsonObject>().Where(s=>S(s,"status")=="on"))
            foreach(var net in (s["networks"] as JsonArray??new()).OfType<JsonObject>().Where(n=>S(n,"type")=="public"))
                foreach(var ip in (net["ips"] as JsonArray??new()).OfType<JsonObject>().Where(i=>S(i,"type")=="ipv4"&&PublicAddress(S(i,"ip"),"ipv4")))names.Add(S(ip,"ip"));
        var ordered=names.OrderByDescending(n=>n.Split('.')[0].Contains("orchestra",StringComparison.OrdinalIgnoreCase)).ThenBy(n=>n,StringComparer.OrdinalIgnoreCase).Take(80).ToArray();
        var found=new System.Collections.Concurrent.ConcurrentBag<(string id,string origin)>();
        await Parallel.ForEachAsync(ordered,new ParallelOptions{MaxDegreeOfParallelism=10},async (host,ct)=>{
            try{
                using var limit=CancellationTokenSource.CreateLinkedTokenSource(ct);limit.CancelAfter(TimeSpan.FromSeconds(4));
                using var response=await discoverHttp.GetAsync("https://"+host+"/health/live",HttpCompletionOption.ResponseHeadersRead,limit.Token);
                if(!response.IsSuccessStatusCode)return;
                using var stream=await response.Content.ReadAsStreamAsync(limit.Token);using var buffer=new MemoryStream();var bytes=new byte[4096];int n;
                while((n=await stream.ReadAsync(bytes.AsMemory(),limit.Token))>0){if(buffer.Length+n>16384)return;buffer.Write(bytes,0,n);}
                var h=JsonNode.Parse(buffer.ToArray()) as JsonObject;if(h==null)return;
                var id=RealId(S(h,"node"));if(id!=""&&Role(S(h,"kind"))=="Orchestra"&&S(h,"status")=="alive"){found.Add((id,"https://"+host));ProtectOrigin("https://"+host);}
            }catch(Exception){} // a failed candidate is not a discovered node
        });
        var candidates=found.GroupBy(x=>x.id).Select(g=>new{id=g.Key,origin=g.OrderByDescending(x=>x.origin.Contains("orchestra",StringComparison.OrdinalIgnoreCase)).ThenBy(x=>IPAddress.TryParse(new Uri(x.origin).Host,out _)).ThenBy(x=>x.origin,StringComparer.Ordinal).First().origin,aliases=g.Select(x=>x.origin).Order().ToArray()}).OrderBy(x=>x.origin).ToArray();
        return new {candidates,checkedCount=ordered.Length,truncated=names.Count>ordered.Length||zones.Count(DomainUsable)>activeZones.Length||failedZones>0||((cloud["servers"] as JsonArray)?.Count??0)>=100};
    }
}
