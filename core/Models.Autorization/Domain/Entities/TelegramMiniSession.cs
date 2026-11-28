namespace Models.Autorization.Domain.Entities;

/// <summary>Restricted Mini App session and one-use proof receipt. No raw initData or bearer stored.</summary>
public sealed class TelegramMiniSession
{
    public string ProofKey { get; set; } = "";
    public string TokenHash { get; set; } = "";
    public string BotId { get; set; } = "";
    public string Subject { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTime LinkedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime RetainUntilUtc { get; set; }
    public bool Revoked { get; set; }
}
