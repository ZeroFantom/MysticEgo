namespace Models.Autorization.Domain.Entities;

public sealed class ChatRpConsent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public string BotId { get; set; } = "";
    public string Peer { get; set; } = "";
    public string EventId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string ActorName { get; set; } = "";
    public string TargetName { get; set; } = "";
    public int Action { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public bool Consumed { get; set; }
}
