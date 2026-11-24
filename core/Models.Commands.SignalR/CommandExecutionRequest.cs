namespace Models.Commands.SignalR;

/// <summary>
/// Сервер → клиент: выполнить команду.
/// Одна команда — один вызов. Сериализуется целиком.
/// </summary>
public sealed record CommandExecutionRequest
{
    /// <summary>Уникальный ID выполнения. Для корреляции логов.</summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>Что выполнить.</summary>
    public required CommandId CommandId { get; init; }

    /// <summary>
    /// Сырые аргументы в виде строки-хвоста: "red #FF0000".
    /// Используется CLI-командами и парсерами.
    /// </summary>
    public string RawArgs { get; init; } = "";

    /// <summary>
    /// Именованные аргументы: {"color": "#FF0000"}.
    /// Используется MCP/HTTP-вызовами.
    /// </summary>
    public IReadOnlyDictionary<string, object?>? NamedArgs { get; init; }

    /// <summary>Кто инициировал: id пользователя.</summary>
    public Guid? CallerId { get; init; }

    /// <summary>Логин инициатора — для логов.</summary>
    public string? CallerLogin { get; init; }

    /// <summary>Момент постановки в очередь.</summary>
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;

    /// <summary>Таймаут выполнения. null — дефолт клиента.</summary>
    public int? TimeoutSeconds { get; init; }
}