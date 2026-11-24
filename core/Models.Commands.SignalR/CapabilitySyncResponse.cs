namespace Models.Commands.SignalR;

/// <summary>Ответ сервера на sync.</summary>
public sealed record CapabilitySyncResponse
{
    /// <summary>Сколько capabilities принято.</summary>
    public int Accepted { get; init; }

    /// <summary>Сервер узнал клиента (guid).</summary>
    public Guid ClientId { get; init; }

    /// <summary>Момент обработки (UTC).</summary>
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;

    public static CapabilitySyncResponse Ack(int count, Guid clientId) =>
        new() { Accepted = count, ClientId = clientId };
}