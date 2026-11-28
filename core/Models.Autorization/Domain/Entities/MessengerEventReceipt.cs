namespace Models.Autorization.Domain.Entities;

/// <summary>Replay receipt only. No message text, sender, link code or provider secret.</summary>
public sealed class MessengerEventReceipt
{
    public string Provider { get; set; } = "";
    public string BotId { get; set; } = "";
    public string EventId { get; set; } = "";
    public DateTime ReceivedUtc { get; set; }
}
