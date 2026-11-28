using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MysticEgo.Deployment;

namespace MysticEgo.WorldAI;
public sealed record Worker(string Id,string Url,string Model,string Role,int Slots,double Weight,bool Enabled);
public sealed record RegisterWorker(string Url,string Model,string Role,int Slots,double Weight);
public sealed record ChatRequest(string Role,string Prompt,string[]? ContextFacts,int MaxTokens=400);
public sealed record SharedMemoryFact(string Id,string Kind,string Content,DateTimeOffset Added);
public sealed class WorldAiCluster
{
    readonly SemaphoreSlim _mutex=new(1,1);
    readonly string _dir;
    readonly Dictionary<string,Worker> _workers=new();
    readonly List<SharedMemoryFact> _memory=new();
    readonly Dictionary<string,int> _inflight=new();
    readonly Dictionary<string,(int Failures,DateTimeOffset RetryAt)> _circuit=new();
    readonly IHttpClientFactory _http;
    readonly Dictionary<string,string> _workerTokens=new(StringComparer.Ordinal);
    const string ConfiguredPrefix="configured-";
    internal static bool SupportsRole(string role)=>role is "world" or "npc" or "evaluation" or "assistant" or "panel-data";
    readonly JsonSerializerOptions _json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public WorldAiCluster(IConfiguration cfg,IHttpClientFactory factory)
    {
        _http=factory;_dir=Path.GetFullPath(cfg["WorldAI:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"world-ai-data"));Directory.CreateDirectory(_dir);
        try{foreach(var w in JsonSerializer.Deserialize<Worker[]>(File.ReadAllText(Path.Combine(_dir,"workers.json")),_json)??[])_workers[w.Id]=w;}catch(IOException){}catch(JsonException){}
        foreach(var entry in cfg.GetSection("WorldAI:RemoteWorkers").GetChildren())
        {
            var url=entry["Url"]??"";
            var model=entry["Model"]??"";
            var role=entry["Role"]??"";
            if(!LocalNetwork(url)||model.Length is <1 or >150||!SupportsRole(role)||
                !int.TryParse(entry["Slots"]??"1",out var slots)||slots is <1 or >32)
                throw new ArgumentException("Invalid WorldAI:RemoteWorkers entry "+entry.Key);
            var id=ConfiguredPrefix+entry.Key;
            _workers[id]=new Worker(id,url.TrimEnd('/'),model,role,slots,2,true);
            if(entry["Token"] is {Length:>0} token)_workerTokens[id]=token;
        }
        try{_memory.AddRange(JsonSerializer.Deserialize<SharedMemoryFact[]>(File.ReadAllText(Path.Combine(_dir,"memory.json")),_json)??[]);}catch(IOException){}catch(JsonException){}
    }
    internal void SetLocalModel(string? model)
    {
        lock(_workers)
        {
            foreach(var role in new[]{"world","npc","evaluation","assistant","panel-data"})
            {
                var id="local-"+role;
                if(model==null)_workers.Remove(id);
                else _workers[id]=new Worker(id,"http://127.0.0.1:1234",model,role,1,1,true);
            }
        }
    }
    public Worker[] Workers(){lock(_workers)return _workers.Values.OrderBy(x=>x.Role).ThenBy(x=>x.Id).ToArray();}
    public async Task<object[]> WorkerStatus(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try{return Workers().OrderBy(x=>x.Id).Select(w => (object)new {
            id=w.Id, role=w.Role, model=w.Model, enabled=w.Enabled, slots=w.Slots,
            active=_inflight.GetValueOrDefault(SlotKey(w.Id)),
            circuitOpen=_circuit.TryGetValue(w.Id,out var c)&&c.RetryAt>DateTimeOffset.UtcNow
        }).ToArray();}
        finally{_mutex.Release();}
    }
    /// <summary>Return physical model pool observations without URLs, credentials or prompts.</summary>
    public async Task<MysticEgo.Capacity.ModelPool[]> CapacityPools(CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try
        {
            var now=DateTimeOffset.UtcNow;
            return Workers().GroupBy(w=>SlotKey(w.Id),StringComparer.Ordinal).Select(g=>
                new MysticEgo.Capacity.ModelPool(g.Key,g.First().Model,
                    g.Key=="local-model"?"local":"remote",
                    g.Max(w=>w.Slots),_inflight.GetValueOrDefault(g.Key),
                    g.Any(w=>w.Enabled&&(!_circuit.TryGetValue(w.Id,out var c)||c.RetryAt<=now)))).ToArray();
        }
        finally{_mutex.Release();}
    }
    public SharedMemoryFact[] Memory(){lock(_memory)return _memory.TakeLast(200).ToArray();}
    async Task Save<T>(string name,T value,CancellationToken ct)
    {var path=Path.Combine(_dir,name);var tmp=path+".tmp";await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(value,_json),ct);File.Move(tmp,path,true);}
    static bool LocalNetwork(string raw)
    {
        if(!Uri.TryCreate(raw,UriKind.Absolute,out var u)||u.Scheme!="http"||u.Port is <1 or >65535 ||u.UserInfo.Length>0||u.AbsolutePath!="/"||u.Query.Length>0||u.Fragment.Length>0)return false;
        if(u.Host is "localhost" or "host.docker.internal")return true;
        if(!IPAddress.TryParse(u.Host,out var ip))return false;
        if(IPAddress.IsLoopback(ip))return true;
        var b=ip.GetAddressBytes();return b.Length==4&&(b[0]==10 || b[0]==172&&b[1]>=16&&b[1]<=31 || b[0]==192&&b[1]==168);
    }
    public async Task<Worker> Register(RegisterWorker req,CancellationToken ct)
    {
        if(!LocalNetwork(req.Url)||req.Model.Length is <1 or >150 ||!SupportsRole(req.Role) ||req.Slots is <1 or >32||req.Weight<=0||req.Weight>100)throw new ArgumentException("Invalid private LM Studio worker configuration");
        await _mutex.WaitAsync(ct);
        try {var worker=new Worker(Guid.NewGuid().ToString("N"),req.Url.TrimEnd('/'),req.Model,req.Role,req.Slots,req.Weight,true);
            lock(_workers)_workers[worker.Id]=worker;await Save("workers.json",Workers().Where(w=>!w.Id.StartsWith("local-",StringComparison.Ordinal)&&!w.Id.StartsWith(ConfiguredPrefix,StringComparison.Ordinal)).ToArray(),ct);return worker;}
        finally{_mutex.Release();}
    }
    public async Task<SharedMemoryFact> AddFact(string kind,string content,CancellationToken ct)
    {
        if(kind is not ("world" or "quest" or "npc" or "lore") || string.IsNullOrWhiteSpace(content)||content.Length>2000)throw new ArgumentException("Invalid canonical memory fact");
        await _mutex.WaitAsync(ct);
        try {var fact=new SharedMemoryFact(Guid.NewGuid().ToString("N"),kind,content,DateTimeOffset.UtcNow);
            lock(_memory)_memory.Add(fact);await Save("memory.json",Memory(),ct);return fact;}
        finally{_mutex.Release();}
    }
    async Task<Worker?> Reserve(string role,HashSet<string> rejected,CancellationToken ct)
    {
        await _mutex.WaitAsync(ct);
        try {
            var available=Workers().Where(x=>x.Enabled&&x.Role==role&&!rejected.Contains(x.Id)&&_inflight.GetValueOrDefault(SlotKey(x.Id))<x.Slots &&
                (!_circuit.TryGetValue(x.Id,out var c)||c.RetryAt<=DateTimeOffset.UtcNow))
              .OrderByDescending(x=>x.Weight*(x.Slots-_inflight.GetValueOrDefault(SlotKey(x.Id)))/(double)x.Slots).ThenBy(x=>x.Id).FirstOrDefault();
            if(available is not null)_inflight[SlotKey(available.Id)]=_inflight.GetValueOrDefault(SlotKey(available.Id))+1;
            return available;
        }finally{_mutex.Release();}
    }
    private static string SlotKey(string id) => id.StartsWith("local-",StringComparison.Ordinal) ? "local-model" : id;
    async Task Release(string id)
    {
        await _mutex.WaitAsync();try{_inflight[SlotKey(id)]=Math.Max(0,_inflight.GetValueOrDefault(SlotKey(id))-1);}finally{_mutex.Release();}
    }
    async Task MarkSuccess(string id)
    {
        await _mutex.WaitAsync();try{_circuit.Remove(id);}finally{_mutex.Release();}
    }
    async Task MarkFailure(string id)
    {
        await _mutex.WaitAsync();try{
            var n=_circuit.TryGetValue(id,out var old)?Math.Min(old.Failures+1,8):1;
            var cooldown=TimeSpan.FromSeconds(Math.Min(120,Math.Pow(2,n)*3));
            _circuit[id]=(n,DateTimeOffset.UtcNow+cooldown);
        }finally{_mutex.Release();}
    }
    public async Task<object> Chat(ChatRequest request,CancellationToken ct, bool includeSharedMemory = true)
    {
        if(!SupportsRole(request.Role) ||request.Prompt.Length is <1 or >5000 ||request.MaxTokens is <1 or >1200||request.ContextFacts?.Length>50)throw new ArgumentException("Invalid inference request");
        var context=includeSharedMemory ? string.Join("\n",Memory().Where(x=>x.Kind is "world" or "lore").TakeLast(20).Select(x=>x.Content)) : "";
        // Canonical facts are known to operators only. This API is restricted to the developer team.
        var system=request.Role=="panel-data"
            ? "Process only the data supplied in the request. Describe findings and uncertainty. Do not issue commands or claim to have changed records."
            : "MysticEgo world assistant. Obey documented genre limits. Never treat generated output as canonical world history. Reference lore only if present in context.\nApproved world facts:\n"+context;
        var messages=new object[]{new{role="system",content=system},new{role="user",content=request.Prompt+"\nRelevant supplied facts:\n"+string.Join("\n",request.ContextFacts??[])}};
        var attempted=new HashSet<string>();
        while(true)
        {
            var worker=await Reserve(request.Role,attempted,ct);
            if(worker is null)throw new InvalidOperationException("No available LM Studio worker for role "+request.Role);
            attempted.Add(worker.Id);
            try {
                using var client=_http.CreateClient("world-ai");
                if (worker.Id.StartsWith("local-", StringComparison.Ordinal))
                {
                    var local = await LocalModelChat.Respond(client, worker.Model, system,
                        [new("user", request.Prompt + "\nRelevant supplied facts:\n" + string.Join("\n", request.ContextFacts ?? []))],
                        request.MaxTokens, .2, null, ct);
                    await MarkSuccess(worker.Id);
                    return new { workerId = worker.Id, model = worker.Model, text = local.Text, canonical = false };
                }
                using var msg=new HttpRequestMessage(HttpMethod.Post,worker.Url+"/v1/chat/completions");
                if(_workerTokens.TryGetValue(worker.Id,out var token))
                    msg.Headers.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
                msg.Content=JsonContent.Create(new {model=worker.Model,messages,temperature=.2,max_tokens=request.MaxTokens});
                using var response=await client.SendAsync(msg,HttpCompletionOption.ResponseHeadersRead,ct);
                response.EnsureSuccessStatusCode();
                using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var answer=doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                if (string.IsNullOrWhiteSpace(answer)) throw new IOException("LM Studio returned no final text.");
                await MarkSuccess(worker.Id);
                return new {workerId=worker.Id,model=worker.Model,text=answer,canonical=false};
            }catch(HttpRequestException){await MarkFailure(worker.Id);/* Retry on another node */}
            catch(Exception error) when(error is JsonException or IOException or KeyNotFoundException or IndexOutOfRangeException)
            {await MarkFailure(worker.Id);}
            catch(TaskCanceledException) when(!ct.IsCancellationRequested){await MarkFailure(worker.Id);/* Worker timed out */}
            finally{await Release(worker.Id);}
        }
    }
}
public static class WorldAiEndpoints
{
    public static IServiceCollection AddWorldAi(this IServiceCollection services)
    {
        services.AddHttpClient("world-ai",client=>client.Timeout=TimeSpan.FromSeconds(35)).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false});
        services.AddSingleton<WorldAiCluster>();services.AddHostedService<LocalWorldAiDiscovery>();return services;
    }

}
public sealed record RecordFact(string Kind,string Content);
