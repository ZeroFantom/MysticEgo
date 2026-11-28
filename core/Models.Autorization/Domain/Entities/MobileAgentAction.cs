namespace Models.Autorization.Domain.Entities;

public sealed class MobileAgentAction
{
    public Guid Id {get;set;}
    public Guid UserId {get;set;}
    public Guid Source {get;set;}
    public Guid Target {get;set;}
    public string Action {get;set;}="";
    public string State {get;set;}="";
    public string? Result {get;set;}
    public DateTime CreatedUtc {get;set;}
    public DateTime UpdatedUtc {get;set;}
    public DateTime ExpiresUtc {get;set;}
}
