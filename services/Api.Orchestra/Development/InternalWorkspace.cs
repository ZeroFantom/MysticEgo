using Models.Data;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Development;

[Entity("mysticego.orchestra.workspace.item",1)]
public sealed class InternalWorkItem
{
    [EntityKey] public Guid Id {get;set;}
    public string Kind {get;set;}="issue";
    public string Title {get;set;}="";
    public string Body {get;set;}="";
    public string State {get;set;}="open";
    public Guid CreatorId {get;set;}
    public string Creator {get;set;}="";
    public Guid? AssigneeId {get;set;}
    public string? Assignee {get;set;}
    public DateTimeOffset UpdatedAt {get;set;}
    public WorkspaceComment[] Comments {get;set;}=[];
    public string Publication {get;set;}="local";
    public int? GitHubNumber {get;set;}
}

/// <summary>Internal workspace uses existing relational entity storage. Publication is explicit and
/// records an in-flight marker before calling GitHub; ambiguous outcomes require manual reconciliation.</summary>
public sealed class InternalWorkspace(NodeState state,GitHubWorkspace github)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task<InternalWorkItem[]> List(CancellationToken ct)=>
        (await state.Data.ListAsync<InternalWorkItem>(500,ct)).OrderByDescending(x=>x.UpdatedAt).ToArray();
    public async Task<InternalWorkItem> Create(WorkspaceCreate input,Guid actor,string login,CancellationToken ct)
    {
        if(input.Kind is not ("issue" or "discussion"))throw new ArgumentException("invalid_kind");
        var item=new InternalWorkItem{Id=Guid.NewGuid(),Kind=input.Kind,Title=GitHubWorkspace.ValidateText(input.Title,200),
            Body=GitHubWorkspace.ValidateText(input.Body,20000,false),CreatorId=actor,Creator=login,UpdatedAt=DateTimeOffset.UtcNow};
        await gate.WaitAsync(ct);
        try
        {
            if((await List(ct)).Length>=500)throw new WorkspaceProviderException(409,"internal_workspace_limit");
            await state.Data.PutAsync(item,ct);state.Log(login,"workspace.internal.create",item.Id.ToString(),true);return item;
        }
        finally{gate.Release();}
    }
    private async Task<InternalWorkItem> Find(Guid id,CancellationToken ct)=>
        await state.Data.GetAsync<InternalWorkItem>(id,ct)??throw new WorkspaceProviderException(404,"internal_item_not_found");

    public async Task<InternalWorkItem> Change(Guid id,WorkspaceChange input,Guid actor,string login,bool owner,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await Find(id,ct);
            if(item.Publication!="local")throw new WorkspaceProviderException(409,"publication_locked_use_github");
            switch(input.Action)
            {
                case "claim" when item.Kind=="issue"&&item.State=="open"&&(item.AssigneeId is null||item.AssigneeId==actor):
                    item.AssigneeId=actor;item.Assignee=login;break;
                case "release" when item.AssigneeId==actor||owner:
                    item.AssigneeId=null;item.Assignee=null;break;
                case "close" or "reopen" when owner||item.CreatorId==actor||item.AssigneeId==actor:
                    item.State=input.Action=="close"?"closed":"open";break;
                case "comment":
                    if(item.Comments.Length>=100)throw new WorkspaceProviderException(409,"internal_comment_limit");
                    item.Comments=[..item.Comments,new WorkspaceComment(login,GitHubWorkspace.ValidateText(input.Body,2000),DateTimeOffset.UtcNow.ToString("O"))];break;
                default:throw new WorkspaceProviderException(409,"action_not_allowed");
            }
            item.UpdatedAt=DateTimeOffset.UtcNow;await state.Data.PutAsync(item,ct);
            state.Log(login,"workspace.internal."+input.Action,id.ToString(),true);return item;
        }
        finally{gate.Release();}
    }

    /// <summary>Recover a lost publication response by verifying the stable marker in an existing GitHub item.</summary>
    public async Task<InternalWorkItem> Reconcile(Guid id,int number,string login,CancellationToken ct)
    {
        if(number<=0)throw new ArgumentException("invalid_item");
        await gate.WaitAsync(ct);
        try
        {
            var item=await Find(id,ct);
            if(item.Publication!="pending"||!await github.HasPublicationMarker(item.Kind,number,id,ct))
                throw new WorkspaceProviderException(409,"publication_marker_not_verified");
            item.Publication="published";item.GitHubNumber=number;item.UpdatedAt=DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item,ct);state.Log(login,"workspace.internal.reconcile",id.ToString(),true);return item;
        }
        finally{gate.Release();}
    }

    public async Task<InternalWorkItem> Publish(Guid id,string? category,Guid actor,string login,bool owner,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item=await Find(id,ct);
            if(!owner&&item.CreatorId!=actor)throw new WorkspaceProviderException(403,"creator_or_owner_required");
            if(item.Publication!="local"||item.State!="open")throw new WorkspaceProviderException(409,"publication_locked");
            if(!github.CanWrite)throw new WorkspaceProviderException(503,"github_write_disabled");
            // Do not publish internal comments, identities or assignees without a separate review.
            // The marker remains durable if the upstream response is lost or the process dies.
            var body=item.Body+"\n\n<!-- mysticego-workspace:"+item.Id.ToString("D")+" -->";
            GitHubWorkspace.ValidateText(body,20000,false);
            if(item.Kind=="discussion"&&string.IsNullOrWhiteSpace(category))throw new ArgumentException("discussion_category_required");
            if(item.Kind=="discussion"&&!(await github.Categories(ct)).Any(c=>c.Id==category))throw new ArgumentException("invalid_discussion_category");
            item.Publication="pending";item.UpdatedAt=DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item,ct);
            var published=await github.Create(new(item.Kind,item.Title,body,category),login,ct);
            item.Publication="published";item.GitHubNumber=published.Number;item.UpdatedAt=DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item,ct);
            return item;
        }
        finally{gate.Release();}
    }
}
