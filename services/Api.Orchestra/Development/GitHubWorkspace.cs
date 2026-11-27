using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Development;

/// <summary>Single GitHub-backed workspace service. Read snapshots are shared for 15 seconds;
/// successful or uncertain writes invalidate all cached pages. GitHub is authoritative for published items.</summary>
public sealed class GitHubWorkspace(GitHubWorkspaceClient github,IConfiguration config,NodeState state)
{
    private readonly SemaphoreSlim gate=new(1,1);
    private readonly Dictionary<string,WorkspacePage> cache=new(StringComparer.Ordinal);
    private static readonly HttpMethod Patch=new("PATCH");
    private const string DiscussionFields="id number title body closed author{login} category{id name} updatedAt comments{totalCount}";
    private const string RepositoryHead="repository(owner:\"ZeroFantom\",name:\"MysticEgo\")";
    public bool CanWrite=>github.CanWrite;
    public object Connection(Guid actor)=>new{connected=github.CanRead,writable=github.CanWrite,githubLogin=Login(actor),repository=GitHubWorkspaceClient.Repository};

    /// <summary>Only operator-configured, verified GitHub account mappings are used for self-assignment.
    /// A supplied browser login never determines the assignee.</summary>
    public string? Login(Guid actor)
    {
        var login=config["DevelopmentWorkspace:Accounts:"+actor.ToString("D")];
        return login is not null&&Regex.IsMatch(login,@"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})\z")?login:null;
    }
    public static string ValidateText(string? value,int limit,bool required=true)
    {
        if(value is null||value.Length>limit||required&&string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("invalid_workspace_text");
        return value.Trim();
    }
    private static string Text(JsonNode? value,string name)=>value?[name]?.ToString()??"";
    private static int Number(JsonNode? value,string name)=>int.TryParse(Text(value,name),out var n)?n:0;
    private static JsonObject[] Objects(JsonNode? value)=>value is JsonArray rows?rows.OfType<JsonObject>().ToArray():[];
    private static string[] Names(JsonNode? value,string key)=>Objects(value).Select(x=>Text(x,key)).Where(x=>x.Length>0).ToArray();
    private static string Limit(string text,int length)=>text[..Math.Min(text.Length,length)];
    private static bool IsIssue(JsonObject item)=>!item.ContainsKey("pull_request")&&Number(item,"number")>0;
    private static string Url(string kind,int number)=>$"https://github.com/{GitHubWorkspaceClient.Repository}/{(kind=="issue"?"issues":"discussions")}/{number}";
    private static WorkspaceItem Issue(JsonObject row)
    {
        var number=Number(row,"number");
        return new("issue",number,Limit(Text(row,"title"),500),Limit(Text(row,"body"),20000),Text(row,"state")=="closed"?"closed":"open",
            Text(row["user"],"login"),Names(row["assignees"],"login"),Names(row["labels"],"name"),Text(row,"updated_at"),Url("issue",number),null,Number(row,"comments"));
    }
    private static WorkspaceItem Discussion(JsonObject row)
    {
        var number=Number(row,"number");
        return new("discussion",number,Limit(Text(row,"title"),500),Limit(Text(row,"body"),20000),row["closed"]?.GetValue<bool>()==true?"closed":"open",
            Text(row["author"],"login"),[],[],Text(row,"updatedAt"),Url("discussion",number),Text(row["category"],"name"),Number(row["comments"],"totalCount"));
    }
    private static WorkspaceComment Comment(JsonObject row,bool graphql)=>new(Text(row[graphql?"author":"user"],"login"),Limit(Text(row,"body"),20000),Text(row,graphql?"createdAt":"created_at"));

    public async Task<WorkspacePage> List(string kind,int page,string? cursor,CancellationToken ct)
    {
        if(kind is not ("issue" or "discussion")||page is <1 or >100||cursor?.Length>512)throw new ArgumentException("invalid_page");
        if(kind=="issue"&&cursor is not null)throw new ArgumentException("invalid_cursor");
        var key=kind+":"+page+":"+cursor;
        await gate.WaitAsync(ct);
        try
        {
            var now=DateTimeOffset.UtcNow;
            if(cache.TryGetValue(key,out var previous)&&now-previous.FetchedAt<TimeSpan.FromSeconds(15))return previous;
            WorkspacePage result;
            if(kind=="issue")
            {
                var rows=Objects(await github.Rest(HttpMethod.Get,$"issues?state=all&sort=updated&direction=desc&per_page=50&page={page}",null,ct));
                result=new(rows.Where(IsIssue).Select(Issue).ToArray(),page,rows.Length==50&&page<100,null,now);
            }
            else
            {
                var data=await github.Graph("query($after:String){"+RepositoryHead+"{discussions(first:50,after:$after,orderBy:{field:UPDATED_AT,direction:DESC}){nodes{"+DiscussionFields+"} pageInfo{hasNextPage endCursor}}}}",new{after=cursor},false,ct);
                var connection=data["repository"]?["discussions"]??throw new WorkspaceProviderException(502,"discussions_unavailable");
                result=new(Objects(connection["nodes"]).Select(Discussion).ToArray(),page,connection["pageInfo"]?["hasNextPage"]?.GetValue<bool>()==true&&page<100,Text(connection["pageInfo"],"endCursor"),now);
            }
            if(cache.Count>=32)cache.Clear();
            cache[key]=result;return result;
        }
        finally{gate.Release();}
    }

    public async Task<WorkspaceCategory[]> Categories(CancellationToken ct)
    {
        var data=await github.Graph("query{"+RepositoryHead+"{discussionCategories(first:50){nodes{id name}}}}",new{},false,ct);
        return Objects(data["repository"]?["discussionCategories"]?["nodes"]).Select(x=>new WorkspaceCategory(Text(x,"id"),Text(x,"name"))).ToArray();
    }

    private async Task<JsonObject> GetIssue(int number,CancellationToken ct)
    {
        var row=await github.Rest(HttpMethod.Get,$"issues/{number}",null,ct) as JsonObject;
        if(row is null||!IsIssue(row))throw new WorkspaceProviderException(404,"issue_not_found");
        return row;
    }
    private async Task<JsonObject> GetDiscussion(int number,CancellationToken ct)
    {
        var data=await github.Graph("query($number:Int!){"+RepositoryHead+"{discussion(number:$number){"+DiscussionFields+" comments(first:50){nodes{author{login} body createdAt} pageInfo{hasNextPage}}}}}",new{number},false,ct);
        return data["repository"]?["discussion"] as JsonObject??throw new WorkspaceProviderException(404,"discussion_not_found");
    }
    public async Task<WorkspaceDetail> Detail(string kind,int number,int page,CancellationToken ct)
    {
        if(number<=0||page is <1 or >100)throw new ArgumentException("invalid_item");
        if(kind=="issue")
        {
            var row=await GetIssue(number,ct);
            var comments=Objects(await github.Rest(HttpMethod.Get,$"issues/{number}/comments?per_page=50&page={page}",null,ct));
            return new(Issue(row),comments.Select(x=>Comment(x,false)).ToArray(),comments.Length==50&&page<100);
        }
        if(kind!="discussion"||page!=1)throw new ArgumentException("invalid_item");
        var discussion=await GetDiscussion(number,ct);
        return new(Discussion(discussion),Objects(discussion["comments"]?["nodes"]).Select(x=>Comment(x,true)).ToArray(),
            discussion["comments"]?["pageInfo"]?["hasNextPage"]?.GetValue<bool>()==true);
    }

    /// <summary>Check an existing published marker before linking a pending local record.</summary>
    public async Task<bool> HasPublicationMarker(string kind,int number,Guid id,CancellationToken ct)
    {
        var row=kind=="issue"?await GetIssue(number,ct):kind=="discussion"?await GetDiscussion(number,ct):throw new ArgumentException("invalid_kind");
        return Text(row,"body").Contains("<!-- mysticego-workspace:"+id.ToString("D")+" -->",StringComparison.Ordinal);
    }

    /// <summary>Publish an explicit user request. No automatic write retry after an ambiguous provider response.</summary>
    public async Task<WorkspaceItem> Create(WorkspaceCreate input,string actor,CancellationToken ct)
    {
        var title=ValidateText(input.Title,200);var body=ValidateText(input.Body,20000,false);
        await gate.WaitAsync(ct);
        try
        {
            WorkspaceItem result;
            if(input.Kind=="issue")
            {
                var row=await github.Rest(HttpMethod.Post,"issues",new{title,body},ct) as JsonObject??throw new WorkspaceProviderException(502,"github_invalid_response");
                result=Issue(row);
            }
            else if(input.Kind=="discussion")
            {
                var data=await github.Graph("query{"+RepositoryHead+"{id discussionCategories(first:50){nodes{id name}}}}",new{},false,ct);
                var repository=data["repository"]??throw new WorkspaceProviderException(502,"repository_unavailable");
                if(!Objects(repository["discussionCategories"]?["nodes"]).Any(x=>Text(x,"id")==input.Category))
                    throw new ArgumentException("invalid_discussion_category");
                var reply=await github.Graph("mutation($repository:ID!,$category:ID!,$title:String!,$body:String!){createDiscussion(input:{repositoryId:$repository,categoryId:$category,title:$title,body:$body}){discussion{"+DiscussionFields+"}}}",new{repository=Text(repository,"id"),category=input.Category,title,body},true,ct);
                result=Discussion(reply["createDiscussion"]?["discussion"] as JsonObject??throw new WorkspaceProviderException(502,"github_write_outcome_unknown"));
            }
            else throw new ArgumentException("invalid_kind");
            if(result.Number<=0)throw new WorkspaceProviderException(502,"github_write_outcome_unknown");
            state.Log(actor,"workspace.github.create",result.Kind+":"+result.Number,true);
            return result;
        }
        finally{cache.Clear();gate.Release();}
    }

    /// <summary>Mutations are scoped to the fixed repository and validated item type. Close/reopen is owner-only.
    /// Claim uses a configured account mapping and verifies the result from GitHub.</summary>
    public async Task Change(string kind,int number,WorkspaceChange input,Guid actor,string login,bool owner,CancellationToken ct)
    {
        if(number<=0)throw new ArgumentException("invalid_item");
        await gate.WaitAsync(ct);
        try
        {
            if(kind=="issue")
            {
                var issue=await GetIssue(number,ct);
                if(input.Action=="claim")
                {
                    var assignee=Login(actor)??throw new WorkspaceProviderException(409,"github_account_not_linked");
                    var current=Names(issue["assignees"],"login");
                    if(Text(issue,"state")!="open"||current.Any(x=>!x.Equals(assignee,StringComparison.OrdinalIgnoreCase)))
                        throw new WorkspaceProviderException(409,"issue_already_assigned_or_closed");
                    var result=await github.Rest(HttpMethod.Post,$"issues/{number}/assignees",new{assignees=new[]{assignee}},ct);
                    if(!Names(result["assignees"],"login").Contains(assignee,StringComparer.OrdinalIgnoreCase))
                        throw new WorkspaceProviderException(409,"github_assignment_not_confirmed");
                }
                else if(input.Action=="comment")
                    await github.Rest(HttpMethod.Post,$"issues/{number}/comments",new{body=ValidateText(input.Body,10000)},ct);
                else if(input.Action is "close" or "reopen")
                {
                    if(!owner)throw new WorkspaceProviderException(403,"owner_required");
                    await github.Rest(Patch,$"issues/{number}",new{state=input.Action=="close"?"closed":"open"},ct);
                }
                else throw new ArgumentException("invalid_action");
            }
            else if(kind=="discussion"&&input.Action=="comment")
            {
                var row=await GetDiscussion(number,ct);
                await github.Graph("mutation($id:ID!,$body:String!){addDiscussionComment(input:{discussionId:$id,body:$body}){comment{id}}}",
                    new{id=Text(row,"id"),body=ValidateText(input.Body,10000)},true,ct);
            }
            else throw new ArgumentException("invalid_action");
            state.Log(login,"workspace.github."+input.Action,kind+":"+number,true);
        }
        finally{cache.Clear();gate.Release();}
    }
}
