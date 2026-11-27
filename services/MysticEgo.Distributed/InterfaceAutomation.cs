using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Distributed;

// A browser grants a short lease to its own authenticated principal. No API can
// attach to an arbitrary browser, inject scripts, read forms, or invoke backend operations.
public sealed class InterfaceAutomation
{
    private readonly object gate = new();
    private readonly Dictionary<Guid,Session> sessions = new();
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10), Heartbeat = TimeSpan.FromSeconds(15);
    private static readonly Regex Route = new(@"\A/(?:[a-z][a-z0-9-]*(?:/[a-z][a-z0-9-]*)?)?\z");
    public sealed record Target(string Path,string Label);
    public sealed record Frame(string Path,Target[] Targets);
    public sealed record Action(Guid Id,string Path,DateTimeOffset ExpiresAt);
    public sealed record Outcome(Guid Id,string State,string Path);
    public sealed record View(Guid Id,string Panel,DateTimeOffset ExpiresAt,Frame Frame,Action? Pending,Outcome? Last);
    private sealed class Session(string owner,string panel,Guid id,Frame frame)
    {
        public string Owner=owner, Panel=panel;
        public Guid Id=id;
        public DateTimeOffset ExpiresAt=DateTimeOffset.UtcNow+Lifetime, Seen=DateTimeOffset.UtcNow;
        public Frame Frame=frame;
        public Action? Pending;
        public Outcome? Last;
        public View View()=>new(Id,Panel,ExpiresAt,Frame,Pending,Last);
    }
    private static Frame Validate(Frame frame)
    {
        if(frame is null || frame.Path is null || !Route.IsMatch(frame.Path) || frame.Targets is null || frame.Targets.Length>40 ||
           frame.Targets.Any(t=>t is null||t.Path is null||!Route.IsMatch(t.Path)||t.Label is null||t.Label.Length is <1 or >80||t.Label.Any(char.IsControl)))
            throw new AssistantApiException(400,"Invalid interface snapshot.");
        return new(frame.Path,frame.Targets.DistinctBy(t=>t.Path).ToArray());
    }
    private void Sweep()
    {
        var now=DateTimeOffset.UtcNow;
        foreach(var item in sessions.ToArray())
        {
            var s=item.Value;
            if(now>s.ExpiresAt||now-s.Seen>Heartbeat){sessions.Remove(item.Key);continue;}
            if(s.Pending is { } p && now>p.ExpiresAt){s.Last=new(p.Id,"expired",s.Frame.Path);s.Pending=null;}
        }
    }
    private Session Find(string owner,Guid id)
    {
        Sweep();
        if(!sessions.TryGetValue(id,out var s)||s.Owner!=owner)throw new AssistantApiException(404,"Interface session is unavailable. Enable control in the visible window.");
        return s;
    }
    public View Open(string owner,string panel,Frame frame)
    {
        frame=Validate(frame);
        lock(gate)
        {
            Sweep();
            if(sessions.Count>=128||sessions.Values.Count(s=>s.Owner==owner)>=4)throw new AssistantApiException(429,"Too many interface sessions.");
            var id=Guid.NewGuid();var session=new Session(owner,panel,id,frame);sessions.Add(id,session);return session.View();
        }
    }
    public View Poll(string owner,Guid id,Frame frame,Outcome? result)
    {
        frame=Validate(frame);
        lock(gate)
        {
            var s=Find(owner,id);s.Seen=DateTimeOffset.UtcNow;s.Frame=frame;
            if(result is not null&&s.Pending is { } p&&result.Id==p.Id)
            {
                var ok=result.State=="done"&&frame.Path==p.Path;
                s.Last=new(p.Id,ok?"done":"failed",frame.Path);s.Pending=null;
            }
            return s.View();
        }
    }
    public View[] List(string owner){lock(gate){Sweep();return sessions.Values.Where(s=>s.Owner==owner).Select(s=>s.View()).ToArray();}}
    public View Inspect(string owner,Guid id){lock(gate)return Find(owner,id).View();}
    public View Navigate(string owner,Guid id,string path)
    {
        lock(gate)
        {
            var s=Find(owner,id);
            if(s.Pending is not null)throw new AssistantApiException(409,"An action is already pending.");
            if(!s.Frame.Targets.Any(t=>t.Path==path))throw new AssistantApiException(400,"The route is not an available navigation control.");
            s.Pending=new(Guid.NewGuid(),path,DateTimeOffset.UtcNow.AddSeconds(10));return s.View();
        }
    }
    public void Close(string owner,Guid id){lock(gate){Find(owner,id);sessions.Remove(id);}}
}

[ApiController,Authorize(Policy="operator"),Route("api/v1/interface-control"),RequestSizeLimit(16384),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class InterfaceControlController(InterfaceAutomation service,IAssistantAccessPolicy access,NodeOptions node):ControllerBase
{
    private string? Owner()
    {
        var user=access.ResolveOwner(User);
        var subject=User.FindFirst(ClaimTypes.NameIdentifier)?.Value??User.FindFirst("sub")?.Value;
        return user is null||string.IsNullOrWhiteSpace(subject)?null:subject+"|"+user;
    }
    private IActionResult Run(Func<string,object> operation)
    {
        var owner=Owner();if(owner is null)return Forbid();
        try{return Ok(operation(owner));}
        catch(AssistantApiException error){return StatusCode(error.StatusCode,new{error=error.Message});}
    }
    public sealed record PollInput(InterfaceAutomation.Frame Frame,InterfaceAutomation.Outcome? Result);
    [HttpPost("sessions")] public IActionResult Open(InterfaceAutomation.Frame frame)=>Run(owner=>service.Open(owner,node.Kind.ToString(),frame));
    [HttpPost("sessions/{id:guid}/poll")] public IActionResult Poll(Guid id,PollInput input)=>Run(owner=>service.Poll(owner,id,input.Frame,input.Result));
    [HttpDelete("sessions/{id:guid}")] public IActionResult Close(Guid id)=>Run(owner=>{service.Close(owner,id);return new{closed=true};});
    [HttpGet("sessions")] public IActionResult List()=>Run(owner=>service.List(owner));
    [HttpPost("mcp")]
    public IActionResult Mcp(JsonObject request)
    {
        var owner=Owner();if(owner is null)return Forbid();
        var id=request["id"]?.DeepClone();
        IActionResult Reply(object result)=>Ok(new{jsonrpc="2.0",id,result});
        IActionResult Error(int code,string message)=>Ok(new{jsonrpc="2.0",id,error=new{code,message}});
        if(request["jsonrpc"]?.ToString()!="2.0")return Error(-32600,"Invalid request");
        var method=request["method"]?.ToString();
        if(id is null&&method?.StartsWith("notifications/",StringComparison.Ordinal)==true)return Accepted();
        if(method=="initialize")return Reply(new{protocolVersion="2025-03-26",capabilities=new{tools=new{}},serverInfo=new{name="mysticego-interface",version="1.0.0"}});
        if(method=="ping")return Reply(new{});
        if(method=="tools/list")return Reply(new{tools=new[]{
            Tool("interface_sessions","List explicitly enabled visible windows",false,false),
            Tool("interface_inspect","Read route, allowed navigation controls and observed last result",true,false),
            Tool("interface_navigate","Click an allowed navigation control in the selected window; inspect afterwards to verify completion",true,true),
            Tool("interface_assert_view","Check that the observed route equals the expected path; does not navigate",true,true)
        }});
        if(method!="tools/call")return Error(-32601,"Method not found");
        if(request["params"] is not JsonObject p)return Error(-32602,"Invalid params");
        var name=p["name"]?.ToString();
        var args=p["arguments"] as JsonObject;
        object result;
        try
        {
            if(name=="interface_sessions")result=service.List(owner);
            else
            {
                if(!Guid.TryParse(args?["sessionId"]?.ToString(),out var session))return Error(-32602,"Invalid session ID");
                result=name switch{
                    "interface_inspect"=>service.Inspect(owner,session),
                    "interface_navigate"=>service.Navigate(owner,session,args?["path"]?.ToString()??""),
                    "interface_assert_view"=>AssertView(service.Inspect(owner,session),args?["path"]?.ToString()??""),
                    _=>throw new AssistantApiException(400,"Unknown tool")
                };
            }
        }
        catch(AssistantApiException error){return Reply(new{content=new[]{new{type="text",text=error.Message}},isError=true});}
        return Reply(new{content=new[]{new{type="text",text=System.Text.Json.JsonSerializer.Serialize(result,new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))}},isError=false});
    }
    private static object AssertView(InterfaceAutomation.View view,string expected)=>new {passed=view.Frame.Path==expected,expected,actual=view.Frame.Path,pending=view.Pending is not null};
    private static object Tool(string name,string description,bool session,bool navigation)
    {
        var properties=new Dictionary<string,object>();var required=new List<string>();
        if(session){properties["sessionId"]=new{type="string",format="uuid"};required.Add("sessionId");}
        if(navigation){properties["path"]=new{type="string",description="Exact path from interface_inspect"};required.Add("path");}
        return new{name,description,inputSchema=new{type="object",properties,required,additionalProperties=false},annotations=new{readOnlyHint=name!="interface_navigate",destructiveHint=false}};
    }
}
