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
    public string[] RemoteAssignees {get;set;}=[];
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
        await gate.WaitAsync(ct);
        try
        {
            if((await All(ct)).Length>=5000)throw new InvalidOperationException("Workspace capacity reached");
            var item=new WorkspaceItem{Id=Guid.NewGuid(),Creator=actor,Kind=input.Kind,Title=input.Title.Trim(),Body=input.Body,UpdatedAt=DateTimeOffset.UtcNow};
            await state.Data.PutAsync(item,ct);
            state.Log(actor.ToString(),"workspace.create",item.Id.ToString(),true);
            return item;
        }
        finally{gate.Release();}
    }
    public async Task<WorkspaceItem> Change(Guid id,Guid actor,bool owner,string action,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await state.Data.GetAsync<WorkspaceItem>(id,ct)??throw new KeyNotFoundException();
            if(item.SyncState=="synced")return await ChangeRemote(item,actor,owner,action,ct);
            if(item.SyncState!="local")throw new InvalidOperationException("Publication outcome unresolved");
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
    /// <summary>Update remote issue through the configured member mapping, never an arbitrary supplied GitHub login.</summary>
    private async Task<WorkspaceItem> ChangeRemote(WorkspaceItem item,Guid actor,bool owner,string action,CancellationToken ct)
    {
        if(item.Kind!="issue"||item.GitHubNumber is not int number||!github.WritesEnabled)throw new InvalidOperationException("Remote task unavailable");
        var login=github.LinkedLogin(actor);
        if(string.IsNullOrWhiteSpace(login)||!System.Text.RegularExpressions.Regex.IsMatch(login,@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z"))throw new InvalidOperationException("GitHub member mapping required");
        var route="issues/"+number;
        var current=await github.Rest(HttpMethod.Get,route,null,ct);
        if(current["pull_request"] is not null||current["state"]?.ToString()!="open")throw new InvalidOperationException("Remote issue is not open");
        var assignees=current["assignees"]?.AsArray().Select(x=>x?["login"]?.ToString()).ToArray()??[];
        var mine=assignees.Contains(login,StringComparer.OrdinalIgnoreCase);
        object patch=action switch
        {
            "take" when assignees.Length==0=>new{assignees=new[]{login}},
            "release" when mine&&assignees.Length==1=>new{assignees=Array.Empty<string>()},
            "complete" when mine||owner=>new{state="closed"},
            _=>throw new InvalidOperationException("Remote issue action not allowed")
        };
        var updated=await github.Rest(HttpMethod.Patch,route,patch,ct);
        var assigned=updated["assignees"]?.AsArray().Select(x=>x?["login"]?.ToString()).OfType<string>().ToArray()??[];
        if(action=="take"&&!assigned.Contains(login,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("GitHub did not assign the mapped member");
        item.Status=updated["state"]?.ToString()=="closed"?"closed":assigned.Length>0?"in-progress":"open";
        item.RemoteAssignees=assigned;item.Assignee=null;item.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(item,ct);state.Log(actor.ToString(),"workspace.remote."+action,item.Id.ToString(),true);return item;
    }
    public sealed record DiscussionCategory(string Id,string Name);
    private async Task<(string Repository,DiscussionCategory[] Categories)> CategoryData(CancellationToken ct)
    {
        var data=await github.Graph("query { repository(owner:\"ZeroFantom\",name:\"MysticEgo\"){ id discussionCategories(first:50){nodes{id name}} } }",new{},false,ct);
        var repo=data["repository"]??throw new HttpRequestException("Repository unavailable");
        var categories=(repo["discussionCategories"]?["nodes"]?.AsArray().OfType<JsonObject>()??[])
            .Select(x=>new DiscussionCategory(x["id"]?.ToString()??"",x["name"]?.ToString()??""))
            .Where(x=>x.Id.Length is >0 and <=200&&x.Name.Length is >0 and <=200).ToArray();
        return (repo["id"]?.ToString()??throw new HttpRequestException("Missing repository ID"),categories);
    }
    public async Task<DiscussionCategory[]> Categories(CancellationToken ct)=>(await CategoryData(ct)).Categories;
    private static string PublicationBody(WorkspaceItem item)=>item.Body+"\n\n<!-- mysticego-workspace:"+item.Id.ToString("D")+" -->";
    public static bool MatchesPublication(WorkspaceItem item,string title,string body)=>item.SyncState=="publishing"&&
        string.Equals(item.Title,title,StringComparison.Ordinal)&&string.Equals(PublicationBody(item),body,StringComparison.Ordinal);
    /// <summary>Owner-only recovery of an exact remote publication; no automatic linking or repeated create.</summary>
    public async Task<WorkspaceItem> Reconcile(Guid id,int number,Guid actor,bool owner,CancellationToken ct)
    {
        if(!owner)throw new UnauthorizedAccessException();
        if(number<=0)throw new ArgumentException("Invalid remote number");
        await gate.WaitAsync(ct);
        try
        {
            var item=await state.Data.GetAsync<WorkspaceItem>(id,ct)??throw new KeyNotFoundException();
            if(item.SyncState!="publishing"||item.GitHubNumber is not null)throw new InvalidOperationException("Not an unresolved publication");
            var rows=await All(ct);
            if(rows.Any(x=>x.Id!=id&&x.Kind==item.Kind&&x.GitHubNumber==number))throw new InvalidOperationException("Remote item already linked; preserve both records for manual review");
            JsonNode remote;
            if(item.Kind=="issue")
            {
                remote=await github.Rest(HttpMethod.Get,"issues/"+number,null,ct);
                if(remote["pull_request"] is not null)throw new InvalidOperationException("Pull request is not an issue");
            }
            else
            {
                var data=await github.Graph("query($n:Int!){repository(owner:\"ZeroFantom\",name:\"MysticEgo\"){discussion(number:$n){id number title body closed}}}",new{n=number},false,ct);
                remote=data["repository"]?["discussion"]??throw new KeyNotFoundException();
            }
            if(remote["number"]?.GetValue<int>()!=number||!MatchesPublication(item,remote["title"]?.ToString()??"",remote["body"]?.ToString()??""))
                throw new InvalidOperationException("Remote title and body do not match the exact pending publication");
            item.GitHubNumber=number;
            if(item.Kind=="discussion")item.GitHubId=remote["id"]?.ToString()??throw new HttpRequestException("Missing discussion ID");
            item.SyncState="synced";item.UpdatedAt=DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item,ct);state.Log(actor.ToString(),"workspace.reconcile",id.ToString(),true);return item;
        }
        finally{gate.Release();}
    }
    public async Task<WorkspaceItem> Publish(Guid id,Guid actor,bool owner,CancellationToken ct,string? categoryId=null)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await state.Data.GetAsync<WorkspaceItem>(id,ct)??throw new KeyNotFoundException();
            if(!owner&&item.Creator!=actor)throw new UnauthorizedAccessException();
            if(!github.WritesEnabled||item.SyncState!="local")throw new InvalidOperationException("Publishing disabled or already attempted");
            string? repositoryId=null,selectedCategory=null;
            if(item.Kind=="discussion")
            {
                var metadata=await CategoryData(ct);
                repositoryId=metadata.Repository;
                selectedCategory=metadata.Categories.FirstOrDefault(c=>categoryId is null?c.Name=="General":c.Id==categoryId)?.Id
                    ??throw new ArgumentException("Select a valid discussion category");
            }
            // Persist ambiguous state before network request. Never automatically retry a create.
            item.SyncState="publishing";await state.Data.PutAsync(item,ct);
            var body=PublicationBody(item);
            if(item.Kind=="issue")
            {
                var remote=await github.Rest(HttpMethod.Post,"issues",new{title=item.Title,body},ct);
                item.GitHubNumber=remote["number"]?.GetValue<int>()??throw new HttpRequestException("Missing issue number");
            }
            else
            {
                var result=await github.Graph("mutation($r:ID!,$c:ID!,$t:String!,$b:String!){createDiscussion(input:{repositoryId:$r,categoryId:$c,title:$t,body:$b}){discussion{id number}}}",new{r=repositoryId,c=selectedCategory,t=item.Title,b=body},true,ct);
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
                foreach(var n in data.OfType<JsonObject>().Where(n=>n["pull_request"] is null))await Import(rows,"issue",n["number"]!.GetValue<int>(),null,n["title"]!.ToString(),n["body"]?.ToString()??"",n["state"]?.ToString()=="closed",ct,n["assignees"]?.AsArray().Select(x=>x?["login"]?.ToString()).OfType<string>().ToArray());
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
    private async Task Import(List<WorkspaceItem> rows,string kind,int number,string? remoteId,string title,string body,bool closed,CancellationToken ct,string[]? assignees=null)
    {
        var item=rows.FirstOrDefault(x=>x.Kind==kind&&x.GitHubNumber==number);
        // Correlation markers are untrusted GitHub content. Interrupted publications need manual reconciliation; never link an internal record based on a marker alone.
        // Defer an exact pending publication until owner reconciliation; never automatically associate it.
        if(item is null&&rows.Any(x=>x.Kind==kind&&MatchesPublication(x,title,body)))return;
        if(item is null){if(rows.Count>=5000)throw new InvalidOperationException("Workspace capacity reached");item=new WorkspaceItem{Id=Guid.NewGuid(),Kind=kind};rows.Add(item);}
        title=title[..Math.Min(title.Length,200)];body=body[..Math.Min(body.Length,16000)];
        assignees??=[];
        var status=closed?"closed":assignees.Length>0?"in-progress":"open";
        if(item.SyncState=="synced"&&item.Title==title&&item.Body==body&&item.Status==status&&item.RemoteAssignees.SequenceEqual(assignees))return;
        item.GitHubNumber=number;item.GitHubId=remoteId;item.Title=title;item.Body=body;item.Status=status;item.RemoteAssignees=assignees;item.Assignee=null;item.SyncState="synced";item.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(item,ct);
    }
}

/// <summary>Bounded polling; API reads never trigger an external request.</summary>
public sealed class WorkspaceSyncWorker(DevelopmentWorkspace workspace,WorkspaceSyncSignal signal):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(15));
        var last=DateTimeOffset.MinValue;
        do
        {
            var requested=signal.Consume();
            if(requested||DateTimeOffset.UtcNow-last>=TimeSpan.FromMinutes(2)){await workspace.Sync(ct);last=DateTimeOffset.UtcNow;}
        }while(await timer.WaitForNextTickAsync(ct));
    }
}
