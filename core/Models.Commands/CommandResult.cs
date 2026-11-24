namespace Models.Commands;

/// <summary>
/// Результат выполнения команды на клиенте.
/// Домен-модель, не транспорт. По сети летает
/// CommandExecutionResult (с ExecutionId и Duration).
/// </summary>
public sealed record CommandResult
{
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

    // ── Фабрики ────────────────────────────────────────

    public static CommandResult Ok(string? output = null, object? structured = null) => new()
    {
        Success = true,
        Output = output,
        StructuredOutput = structured,
    };

    public static CommandResult Fail(string code, string message) => new()
    {
        Success = false,
        ErrorCode = code,
        ErrorMessage = message,
    };
}