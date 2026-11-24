namespace Models.Commands.SignalR.Hub;

/// <summary>
/// Контекст ошибки hub-метода.
/// </summary>
public sealed record HubErrorContext
{
    public required string HubName { get; init; }
    public required string Method { get; init; }
    public string? ConnectionId { get; init; }
    public Guid? ClientId { get; init; }
    public Exception Exception { get; init; } = null!;
}