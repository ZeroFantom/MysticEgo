using Models.Data;
using MysticEgo.Distributed;
using System.Text.Json.Nodes;

namespace MysticEgo.Api.Orchestra.Development;

public sealed class WorkspaceComment
{
    public Guid Id {get;set;}
    public Guid? ParentId {get;set;}
    public Guid Actor {get;set;}
    public string Author {get;set;}="";
    public string Body {get;set;}="";
    public DateTimeOffset At {get;set;}
    public string? RemoteId {get;set;}
    public string SyncState {get;set;}="local";
}

[Entity("mysticego.orchestra.development.thread",1)]
public sealed class WorkspaceThread
{
    [EntityKey] public Guid Id {get;set;}
    public List<WorkspaceComment> Comments {get;set;}=[];
    public DateTimeOffset? SyncedAt {get;set;}
}

/// <summary>Bounded local threads. Remote reads are on demand and cached; publication is a distinct audited action.</summary>
public sealed class WorkspaceThreads(NodeState state,WorkspaceGitHubClient github)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public sealed record CommentInput(string Body,Guid? ParentId);
    public sealed record ThreadView(WorkspaceComment[] Comments,DateTimeOffset? SyncedAt,bool RemoteUnavailable,bool Truncated);
    private async Task<WorkspaceItem> Item(Guid id,CancellationToken ct)=>await state.Data.GetAsync<WorkspaceItem>(id,ct)??throw new KeyNotFoundException();
    private async Task<WorkspaceThread> Thread(Guid id,CancellationToken ct)=>await state.Data.GetAsync<WorkspaceThread>(id,ct)??new(){Id=id};
    public async Task<ThreadView> Read(Guid id,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await Item(id,ct);var thread=await Thread(id,ct);
            var failed=false;var truncated=false;
            if(item.SyncState=="synced"&&github.Configured&&
               (thread.SyncedAt is null||DateTimeOffset.UtcNow-thread.SyncedAt>TimeSpan.FromSeconds(60)))
            {
                try
                {
                    truncated=await Refresh(item,thread,ct);
                    thread.SyncedAt=DateTimeOffset.UtcNow;
                    await state.Data.PutAsync(thread,ct);
                }
                catch(Exception e) when(e is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException||e is OperationCanceledException&&!ct.IsCancellationRequested){failed=true;}
            }
            return new(thread.Comments.OrderBy(c=>c.At).ToArray(),thread.SyncedAt,failed,truncated);
        }
        finally{gate.Release();}
    }
    public async Task<WorkspaceComment> Add(Guid id,Guid actor,string login,CommentInput input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.Body)||input.Body.Length>8000)throw new ArgumentException("Invalid comment");
        await gate.WaitAsync(ct);
        try
        {
            await Item(id,ct);var thread=await Thread(id,ct);
            if(thread.Comments.Count>=500)throw new InvalidOperationException("Thread capacity reached");
            if(input.ParentId.HasValue&&!thread.Comments.Any(x=>x.Id==input.ParentId))throw new ArgumentException("Unknown parent");
            var comment=new WorkspaceComment{Id=Guid.NewGuid(),Actor=actor,Author=login,Body=input.Body.Trim(),ParentId=input.ParentId,At=DateTimeOffset.UtcNow};
            thread.Comments.Add(comment);await state.Data.PutAsync(thread,ct);state.Log(actor.ToString(),"workspace.comment",id.ToString(),true);return comment;
        }
        finally{gate.Release();}
    }
    public async Task<WorkspaceComment> Publish(Guid id,Guid commentId,Guid actor,bool owner,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await Item(id,ct);var thread=await Thread(id,ct);
            var comment=thread.Comments.SingleOrDefault(c=>c.Id==commentId)??throw new KeyNotFoundException();
            if(!owner&&comment.Actor!=actor)throw new UnauthorizedAccessException();
            if(!github.WritesEnabled||item.SyncState!="synced"||item.GitHubNumber is not int number||comment.SyncState!="local")throw new InvalidOperationException("Publication unavailable");
            var parent=thread.Comments.SingleOrDefault(c=>c.Id==comment.ParentId);
            if(parent is not null&&(item.Kind=="issue"||parent.RemoteId is null||parent.ParentId is not null))
                throw new InvalidOperationException("Only top-level GitHub discussion comments support native replies");
            if(item.Kind=="discussion"&&string.IsNullOrEmpty(item.GitHubId))throw new InvalidOperationException("Missing discussion identifier");
            comment.SyncState="publishing";await state.Data.PutAsync(thread,ct);
            var body=comment.Body+"\n\n<!-- mysticego-comment:"+comment.Id.ToString("D")+" -->";
            JsonNode remote;
            if(item.Kind=="issue")remote=await github.Rest(HttpMethod.Post,$"issues/{number}/comments",new{body},ct);
            else
            {
                var result=await github.Graph("mutation($id:ID!,$body:String!,$parent:ID){addDiscussionComment(input:{discussionId:$id,body:$body,replyToId:$parent}){comment{id}}}",new{id=item.GitHubId,body,parent=parent?.RemoteId},true,ct);
                remote=result["addDiscussionComment"]!["comment"]!;
            }
            comment.RemoteId=remote["id"]?.ToString()??throw new HttpRequestException("Missing comment identity");
            comment.SyncState="synced";thread.SyncedAt=null;await state.Data.PutAsync(thread,ct);
            state.Log(actor.ToString(),"workspace.comment.publish",commentId.ToString(),true);return comment;
        }
        finally{gate.Release();}
    }
    private async Task<bool> Refresh(WorkspaceItem item,WorkspaceThread thread,CancellationToken ct)
    {
        if(item.Kind=="issue")
        {
            for(var page=1;page<=3;page++)
            {
                var rows=(await github.Rest(HttpMethod.Get,$"issues/{item.GitHubNumber}/comments?per_page=100&page={page}",null,ct)).AsArray();
                foreach(var row in rows.OfType<JsonObject>())Merge(thread,row["id"]!.ToString(),row["body"]?.ToString()??"",row["user"]?["login"]?.ToString(),row["created_at"]?.ToString(),null);
                if(rows.Count<100)return false;
            }
            return true;
        }
        var data=await github.Graph("query($id:ID!){node(id:$id){... on Discussion{comments(first:50){pageInfo{hasNextPage} nodes{id body createdAt author{login} replies(first:20){pageInfo{hasNextPage} nodes{id body createdAt author{login}}}}}}}}",new{id=item.GitHubId},false,ct);
        var comments=data["node"]?["comments"]??throw new HttpRequestException("Discussion unavailable");
        var truncated=comments["pageInfo"]?["hasNextPage"]?.GetValue<bool>()==true;
        foreach(var row in comments["nodes"]!.AsArray().OfType<JsonObject>())
        {
            var parent=Merge(thread,row["id"]!.ToString(),row["body"]?.ToString()??"",row["author"]?["login"]?.ToString(),row["createdAt"]?.ToString(),null);
            if(parent is null){truncated=true;continue;}
            var replies=row["replies"]!;
            truncated|=replies["pageInfo"]?["hasNextPage"]?.GetValue<bool>()==true;
            foreach(var reply in replies["nodes"]!.AsArray().OfType<JsonObject>())Merge(thread,reply["id"]!.ToString(),reply["body"]?.ToString()??"",reply["author"]?["login"]?.ToString(),reply["createdAt"]?.ToString(),parent.Id);
        }
        return truncated||thread.Comments.Count>=500;
    }
    private static WorkspaceComment? Merge(WorkspaceThread thread,string remoteId,string body,string? author,string? at,Guid? parent)
    {
        var comment=thread.Comments.FirstOrDefault(c=>c.RemoteId==remoteId);
        // Do not trust a marker supplied by arbitrary repository contributors as proof of identity.
        if(comment is null)
        {
            if(thread.Comments.Count>=500)return null;
            comment=new(){Id=Guid.NewGuid(),RemoteId=remoteId,SyncState="synced"};
            thread.Comments.Add(comment);
        }
        comment.Body=body.Length>8000?body[..8000]:body;
        comment.Author=author??"GitHub";comment.ParentId=parent;
        comment.At=DateTimeOffset.TryParse(at,out var timestamp)?timestamp:DateTimeOffset.UtcNow;
        return comment;
    }
}
