namespace Models.Autorization.Domain.Entities;

public sealed class ChatRpPreference
{
    public string Provider { get; set; } = "";
    public string BotId { get; set; } = "";
    public string Peer { get; set; } = "";
    // "0" is the group switch; positive IDs are individual preferences.
    public string Subject { get; set; } = "";
    public bool Enabled { get; set; }
    public DateTime ChangedUtc { get; set; }
}
