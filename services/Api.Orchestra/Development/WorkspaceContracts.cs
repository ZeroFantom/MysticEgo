namespace MysticEgo.Api.Orchestra.Development;

/// <summary>Sanitized repository task or discussion; number is scoped to repository and kind.</summary>
public sealed record WorkspaceItem(string Kind,int Number,string Title,string Body,string State,string Author,string[] Assignees,string[] Labels,
    string UpdatedAt,string Url,string? Category,int CommentCount);
public sealed record WorkspaceComment(string Author,string Body,string CreatedAt);
public sealed record WorkspaceCategory(string Id,string Name);
public sealed record WorkspacePage(WorkspaceItem[] Items,int Page,bool HasMore,string? Cursor,DateTimeOffset FetchedAt);
public sealed record WorkspaceDetail(WorkspaceItem Item,WorkspaceComment[] Comments,bool MoreComments);
public sealed record WorkspaceCreate(string Kind,string Title,string Body,string? Category);
public sealed record WorkspaceChange(string Action,string? Body=null);
