namespace Models.Autorization.Domain.Entities;

public sealed class AccountMobileAgent
{
    public Guid Id {get;set;}
    public Guid UserId {get;set;}
    public string SecretHash {get;set;}="";
    public string Name {get;set;}="";
    public string Os {get;set;}="";
    public DateTime CreatedUtc {get;set;}
    public DateTime? LastSeenUtc {get;set;}
    public DateTime? RevokedUtc {get;set;}
}
