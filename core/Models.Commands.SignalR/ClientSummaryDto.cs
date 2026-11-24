namespace Models.Commands.SignalR;

/// <summary>Краткая карточка клиента для админки.</summary>
public sealed record ClientSummaryDto
{
    public required Guid ClientId { get; init; }
    public string MachineName { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string Version { get; init; } = "0.0.0";
    public int CapabilityCount { get; init; }
    public bool Online { get; init; }
    public DateTime? LastSeen { get; init; }
    public string? ConnectionId { get; init; }
}