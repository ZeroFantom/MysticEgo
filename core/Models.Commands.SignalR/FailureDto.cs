namespace Models.Commands.SignalR;

/// <summary>Известная возможная ошибка.</summary>
public sealed record FailureDto(string Code, string Description, string? Recovery);