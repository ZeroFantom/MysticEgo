namespace Models.Autorization.Domain.Entities;

public enum ChatRpDeliveryState { Pending, Sending, Sent, Uncertain, Rejected }

/// <summary>Short-lived group delivery record; never stores original group messages.</summary>
public sealed class ChatRpDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public string BotId { get; set; } = "";
    public string Peer { get; set; } = "";
    public string EventId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string TargetId { get; set; } = "";
    public string Caption { get; set; } = "";
    public string Category { get; set; } = "";
    public ChatRpDeliveryState Status { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
}
