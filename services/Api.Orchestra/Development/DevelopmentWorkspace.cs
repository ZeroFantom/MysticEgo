using Models.Data;
using MysticEgo.Distributed;
using System.Text.Json.Nodes;

namespace MysticEgo.Api.Orchestra.Development;

[Entity("mysticego.orchestra.development.task",1)]
public sealed class WorkspaceItem
{
    [EntityKey] public Guid Id {get;set;}
    public string Kind {get;set;}="issue";
    public string Title {get;set;}="";
    public string Body {get;set;}="";
    public string Status {get;set;}="open";
    public Guid Creator {get;set;}
    public Guid? Assignee {get;set;}
    public int? GitHubNumber {get;set;}
    public string? GitHubId {get;set;}
    public string SyncState {get;set;}="local";
    public DateTimeOffset UpdatedAt {get;set;}
}

/// <summary>Local-first team workspace. Published content is explicit; remote records use GitHub as source of truth.</summary>
public sealed class DevelopmentWorkspace(NodeState state,WorkspaceGitHubClient github)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private DateTimeOffset lastSync;
    private string? syncError;
    public sealed record CreateInput(string Kind,string Title,string Body);
    public sealed record View(WorkspaceItem[] Items,bool Connected,bool WritesEnabled,DateTimeOffset LastSync,string? SyncError);
    private Task<WorkspaceItem[]> All(CancellationToken ct)=>ReadAll(ct);
    private async Task<WorkspaceItem[]> ReadAll(CancellationToken ct)=>(await state.Data.ListAsync<WorkspaceItem>(5000,ct)).ToArray();
    public async Task<View> List(CancellationToken ct)=>new((await All(ct)).OrderByDescending(x=>x.UpdatedAt).ToArray(),github.Configured,github.WritesEnabled,lastSync,syncError);
    public async Task<WorkspaceItem> Create(Guid actor,CreateInput input,CancellationToken ct)
    {
        if(input.Kind is not ("issue" or "discussion")||string.IsNullOrWhiteSpace(input.Title)||input.Title.Length>200||input.Body is null||input.Body.Length>16000)throw new ArgumentException("Invalid title, kind or body");
        var item=new WorkspaceItem{Id=Guid.NewGuid(),Creator=actor,Kind=input.Kind,Title=input.Title.Trim(),Body=input.Body,UpdatedAt=DateTimeOffset.UtcNow};
        await state.Data.PutAsync(item,ct);
        state.Log(actor.ToString(),"workspace.create",item.Id.ToString(),true);
        return item;
    }
    public async Task<WorkspaceItem> Change(Guid id,Guid actor,bool owner,string action,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await state.Data.GetAsync<WorkspaceItem>(id,ct)??throw new KeyNotFoundException();
            if(item.SyncState!="local")throw new InvalidOperationException("Published tasks are managed on GitHub or via explicit synchronized actions");
            if(item.Kind!="issue")throw new InvalidOperationException("Only issues have work status");
            switch(action)
            {
                case "take" when item.Status=="open"&&item.Assignee is null:item.Assignee=actor;item.Status="in-progress";break;
                case "release" when item.Status=="in-progress"&&(item.Assignee==actor||owner):item.Assignee=null;item.Status="open";break;
                case "complete" when item.Status=="in-progress"&&(item.Assignee==actor||owner):item.Status="closed";break;
                default:throw new InvalidOperationException("Action not allowed in current state");
            }
            item.UpdatedAt=DateTimeOffset.UtcNow;await state.Data.PutAsync(item,ct);state.Log(actor.ToString(),"workspace."+action,id.ToString(),true);return item;
        }
        finally{gate.Release();}
    }
    public async Task<WorkspaceItem> Publish(Guid id,Guid actor,bool owner,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await state.Data.GetAsync<WorkspaceItem>(id,ct)??throw new KeyNotFoundException();
            if(!owner&&item.Creator!=actor)throw new UnauthorizedAccessException();
            if(!github.WritesEnabled||item.SyncState!="local")throw new InvalidOperationException("Publishing disabled or already attempted");
            // Persist ambiguous state before network request. Never automatically retry a create.
            item.SyncState="publishing";await state.Data.PutAsync(item,ct);
            var body=item.Body+"\n\n<!-- mysticego-workspace:"+item.Id.ToString("D")+" -->";
            if(item.Kind=="issue")
            {
                var remote=await github.Rest(HttpMethod.Post,"issues",new{title=item.Title,body},ct);
                item.GitHubNumber=remote["number"]?.GetValue<int>()??throw new HttpRequestException("Missing issue number");
            }
            else
            {
                var meta=await github.Graph("query { repository(owner:\"ZeroFantom\",name:\"MysticEgo\"){ id discussionCategories(first:50){nodes{id name}} } }",new{},false,ct);
                var repository=meta["repository"]!;
                var category=repository["discussionCategories"]?["nodes"]?.AsArray().FirstOrDefault(x=>x?["name"]?.ToString()=="General");
                if(category is null)throw new InvalidOperationException("General discussion category required");
                var result=await github.Graph("mutation($r:ID!,$c:ID!,$t:String!,$b:String!){createDiscussion(input:{repositoryId:$r,categoryId:$c,title:$t,body:$b}){discussion{id number}}}",new{r=repository["id"]!.ToString(),c=category["id"]!.ToString(),t=item.Title,b=body},true,ct);
                var remote=result["createDiscussion"]!["discussion"]!;
                item.GitHubNumber=remote["number"]!.GetValue<int>();item.GitHubId=remote["id"]!.ToString();
            }
            item.SyncState="synced";item.UpdatedAt=DateTimeOffset.UtcNow;await state.Data.PutAsync(item,ct);
            state.Log(actor.ToString(),"workspace.publish",id.ToString(),true);return item;
        }
        finally{gate.Release();}
    }
    public async Task Sync(CancellationToken ct)
    {
        if(!github.Configured||!await gate.WaitAsync(0,ct))return;
        try
        {
            var rows=(await All(ct)).ToList();
            for(var page=1;page<=5;page++)
            {
                var data=(await github.Rest(HttpMethod.Get,"issues?state=all&per_page=100&page="+page,null,ct)).AsArray();
                foreach(var n in data.OfType<JsonObject>().Where(n=>n["pull_request"] is null))await Import(rows,"issue",n["number"]!.GetValue<int>(),null,n["title"]!.ToString(),n["body"]?.ToString()??"",n["state"]?.ToString()=="closed",ct);
                if(data.Count<100)break;
            }
            string? cursor=null;
            for(var page=0;page<5;page++)
            {
                var data=await github.Graph("query($cursor:String){repository(owner:\"ZeroFantom\",name:\"MysticEgo\"){discussions(first:50,after:$cursor,orderBy:{field:UPDATED_AT,direction:DESC}){nodes{id number title body closed} pageInfo{hasNextPage endCursor}}}}",new{cursor},false,ct);
                var list=data["repository"]!["discussions"]!;
                foreach(var n in list["nodes"]!.AsArray().OfType<JsonObject>())await Import(rows,"discussion",n["number"]!.GetValue<int>(),n["id"]!.ToString(),n["title"]!.ToString(),n["body"]?.ToString()??"",n["closed"]?.GetValue<bool>()==true,ct);
                if(list["pageInfo"]!["hasNextPage"]?.GetValue<bool>()!=true)break;
                cursor=list["pageInfo"]!["endCursor"]?.ToString();
            }
            lastSync=DateTimeOffset.UtcNow;syncError=null;
        }
        catch(Exception e) when(e is not OperationCanceledException||!ct.IsCancellationRequested){syncError="GitHub synchronization incomplete";}
        finally{gate.Release();}
    }
    private async Task Import(List<WorkspaceItem> rows,string kind,int number,string? remoteId,string title,string body,bool closed,CancellationToken ct)
    {
        var item=rows.FirstOrDefault(x=>x.Kind==kind&&x.GitHubNumber==number);
        // Reconcile an interrupted publish using its explicit local correlation marker.
        if(item is null)item=rows.FirstOrDefault(x=>x.Kind==kind&&x.SyncState=="publishing"&&body.Contains("<!-- mysticego-workspace:"+x.Id.ToString("D")+" -->",StringComparison.Ordinal));
        if(item is null){item=new WorkspaceItem{Id=Guid.NewGuid(),Kind=kind};rows.Add(item);}
        var status=closed?"closed":"open";
        if(item.SyncState=="synced"&&item.Title==title&&item.Body==body&&item.Status==status)return;
        item.GitHubNumber=number;item.GitHubId=remoteId;item.Title=title;item.Body=body;item.Status=status;item.SyncState="synced";item.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(item,ct);
    }
}

/// <summary>Bounded polling; API reads never trigger an external request.</summary>
public sealed class WorkspaceSyncWorker(DevelopmentWorkspace workspace):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(2));
        do{await workspace.Sync(ct);}while(await timer.WaitForNextTickAsync(ct));
    }
}
