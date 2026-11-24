using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra;

[ApiController, Authorize(Policy="operator"), RequestSizeLimit(16384)]
public sealed class PanelAssistant(DevelopmentTeam team,ManagedNetwork network,GitHubPanel github,
    IConfiguration settings,IHttpClientFactory clients) : ControllerBase
{
    private static readonly Dictionary<string,string> Views=new()
    {
        ["overview"]="/",["nodes"]="/nodes",["deployment"]="/setup",["branches"]="/project-branches",
        ["team"]="/development-team",["models"]="/model-workers"
    };

    private async Task<bool> CanUse(CancellationToken ct)=>team.IsOwner(User)||await team.IsMember(User,ct);

    [HttpPost("api/v1/managed/panel-assistant")]
    public async Task<IActionResult> Chat(Prompt input,CancellationToken ct)
    {
        if(!await CanUse(ct))return Forbid();
        if(string.IsNullOrWhiteSpace(input.Text)||input.Text.Length>3000)return BadRequest(new {error="Введите запрос до 3000 символов."});
        if(settings["PanelAssistant:Model"] is not {Length:>0} model)
            return StatusCode(503,new {error="На сервере Orchestra ещё не настроена локальная модель."});
        try
        {
            using var client=clients.CreateClient("panel-model");
            using var reply=await client.PostAsJsonAsync("http://127.0.0.1:1236/v1/chat/completions",new {
                model,stream=false,temperature=0.2,max_tokens=600,messages=new[]{
                    new {role="system",content="Помогай разработчику работать с панелью MysticEgo Orchestra. Отвечай на языке запроса. Ты не выполняешь изменения на серверах. Для запроса открыть раздел предложи действие. Для чтения реальных данных верни tool из списка panel_overview, panel_latest_release или panel_branches. Верни JSON с полем text и необязательными tool и view. Допустимые view: overview, nodes, deployment, branches, team, models. Пользователь подтвердит переход. Не выдумывай результаты операций."},
                    new {role="user",content=input.Text}
                }
            },ct);
            reply.EnsureSuccessStatusCode();
            var raw=(await reply.Content.ReadFromJsonAsync<JsonNode>(cancellationToken:ct))?["choices"]?[0]?["message"]?["content"]?.ToString();
            if(string.IsNullOrWhiteSpace(raw))return StatusCode(502,new{error="Модель не вернула ответ."});
            JsonObject? parsed=null;
            try{parsed=JsonNode.Parse(raw) as JsonObject;}catch(JsonException){}
            var view=parsed?["view"]?.ToString();
            var text=parsed?["text"]?.ToString()??raw;
            var toolResult=parsed?["tool"]?.ToString() switch
            {
                "panel_overview"=>JsonSerializer.Serialize(Overview()),
                "panel_latest_release"=>JsonSerializer.Serialize(await github.Latest(ct)),
                "panel_branches"=>JsonSerializer.Serialize(await github.Branches(ct)),
                _=>null
            };
            return Ok(new {text,view=view is not null&&Views.ContainsKey(view)?view:null,toolResult});
        }
        catch(Exception e) when(e is HttpRequestException or IOException or JsonException or InvalidOperationException || e is TaskCanceledException&&!ct.IsCancellationRequested)
        {return StatusCode(502,new {error="Локальная модель Orchestra недоступна."});}
    }

    [HttpPost("api/v1/managed/panel-assistant/mcp")]
    public async Task<IActionResult> Mcp(JsonObject request,CancellationToken ct)
    {
        if(!await CanUse(ct))return Forbid();
        var id=request["id"]?.DeepClone();
        IActionResult Reply(object result)=>Ok(new {jsonrpc="2.0",id,result});
        IActionResult Error(int code,string message)=>Ok(new {jsonrpc="2.0",id,error=new {code,message}});
        if(request["jsonrpc"]?.ToString()!="2.0")return Error(-32600,"Invalid Request");
        var method=request["method"]?.ToString();
        if(id is null&&method=="notifications/initialized")return Accepted();
        if(method=="initialize")return Reply(new {protocolVersion="2025-03-26",capabilities=new{tools=new{}},serverInfo=new{name="mysticego-panel",version="1.0.0"}});
        if(method=="ping")return Reply(new{});
        if(method=="tools/list")return Reply(new {tools=new[]{
            Tool("panel_overview","Обзор зарегистрированных Global"),
            Tool("panel_latest_release","Последний опубликованный релиз GitHub"),
            Tool("panel_branches","Список веток GitHub"),
            Tool("panel_open_view","Предложить открыть раздел панели; переход подтверждает пользователь.",true)
        }});
        if(method!="tools/call")return Error(-32601,"Method not found");
        if(request["params"] is not JsonObject parameters)return Error(-32602,"Invalid params");
        try
        {
            object? content;
            switch(parameters["name"]?.ToString())
            {
                case "panel_overview":
                    content=Overview();
                    break;
                case "panel_latest_release": content=await github.Latest(ct);break;
                case "panel_branches": content=await github.Branches(ct);break;
                case "panel_open_view":
                    var view=(parameters["arguments"] as JsonObject)?["view"]?.ToString();
                    if(view is null||!Views.TryGetValue(view,out var path))return Error(-32602,"Unknown view");
                    content=new {view,path,requiresConfirmation=true};
                    break;
                default:return Error(-32602,"Unknown tool");
            }
            return Reply(new {content=new[]{new {type="text",text=JsonSerializer.Serialize(content)}},isError=false});
        }
        catch(Exception e) when(e is HttpRequestException or IOException or JsonException || e is TaskCanceledException&&!ct.IsCancellationRequested)
        {return Reply(new {content=new[]{new {type="text",text="Источник данных временно недоступен."}},isError=true});}
    }

    private object Overview()
    {
        var nodes=network.Nodes().Where(n=>n.Kind=="global").ToArray();
        return new {count=nodes.Length,globals=nodes.Select(n=>new {n.Id,n.Name,n.Enabled}).ToArray()};
    }

    private static object Tool(string name,string description,bool navigation=false)=>new {
        name,description,inputSchema=new {type="object",
            properties=navigation?new Dictionary<string,object>{{"view",new {type="string",@enum=Views.Keys.ToArray()}}}:new Dictionary<string,object>(),
            required=navigation?new[]{"view"}:Array.Empty<string>(),additionalProperties=false},
        annotations=new {readOnlyHint=true,destructiveHint=false}
    };
    public sealed record Prompt(string Text);
}
