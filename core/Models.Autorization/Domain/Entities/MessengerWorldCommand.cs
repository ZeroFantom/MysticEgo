namespace Models.Autorization.Domain.Entities;

public enum MessengerCommandState { Pending, Leased, Completed, Rejected }

/// <summary>Internal handoff only. User text and social identifiers require restricted storage.</summary>
public sealed class MessengerWorldCommand
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = "";
    public string BotId { get; set; } = "";
    public string Subject { get; set; } = "";
    public string EventId { get; set; } = "";
    public string Command { get; set; } = "";
    public DateTime LinkedUtc { get; set; }
    public DateTime OccurredUtc { get; set; }
    public DateTime ReceivedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public MessengerCommandState Status { get; set; }
    public Guid? LeaseId { get; set; }
    public Guid? LeaseOwner { get; set; }
    public DateTime? LeaseUntilUtc { get; set; }
    public int Attempts { get; set; }
}
