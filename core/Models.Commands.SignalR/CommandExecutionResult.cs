namespace Models.Commands.SignalR;

/// <summary>
/// Клиент → сервер: результат выполнения.
/// Универсальная структура — независима от того, что вернула команда.
/// </summary>
public sealed record CommandExecutionResult
{
    /// <summary>Тот же ID, что в запросе.</summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>Что выполнялось.</summary>
    public required CommandId CommandId { get; init; }

    /// <summary>Успешно или нет.</summary>
    public required bool Success { get; init; }

    /// <summary>Текстовый вывод. Может быть null при структурированном ответе.</summary>
    public string? Output { get; init; }

    /// <summary>Структурированный вывод (объект). null — не структурирован.</summary>
    public object? StructuredOutput { get; init; }

    /// <summary>Код ошибки. Только при Success=false.</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Сообщение об ошибке. Только при Success=false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Сколько выполнялось.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Когда завершилось.</summary>
    public DateTime CompletedAt { get; init; } = DateTime.UtcNow;

    // ── Фабрики ────────────────────────────────────

    public static CommandExecutionResult Ok(
        Guid executionId,
        CommandId commandId,
        string? output,
        TimeSpan duration,
        object? structuredOutput = null)
        => new()
        {
            ExecutionId = executionId,
            CommandId = commandId,
            Success = true,
            Output = output,
            StructuredOutput = structuredOutput,
            Duration = duration,
        };

    public static CommandExecutionResult Fail(
        Guid executionId,
        CommandId commandId,
        string errorCode,
        string errorMessage,
        TimeSpan duration)
        => new()
        {
            ExecutionId = executionId,
            CommandId = commandId,
            Success = false,
            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            Duration = duration,
        };
}