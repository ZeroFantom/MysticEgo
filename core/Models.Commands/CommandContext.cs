namespace Models.Commands;

/// <summary>
/// Контекст выполнения команды. Даёт команде всё, что нужно:
/// id вызова, аргументы, инициатора и приёмник прогресса.
///
/// Progress — опционален. ctx.Report(...) безопасен при null.
/// </summary>
public sealed class CommandContext
{
    /// <summary>Уникальный ID выполнения.</summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>Какая команда выполняется.</summary>
    public required CommandId CommandId { get; init; }

    /// <summary>Сырые аргументы строкой: "red #FF0000".</summary>
    public string? RawArgs { get; init; }

    /// <summary>Именованные аргументы: {"color": "#FF0000"}.</summary>
    public IReadOnlyDictionary<string, object?>? NamedArgs { get; init; }

    /// <summary>Кто инициировал (id пользователя).</summary>
    public Guid? CallerId { get; init; }

    /// <summary>Логин инициатора — для логов.</summary>
    public string? CallerLogin { get; init; }

    /// <summary>
    /// Приёмник прогресса. Может быть null — команда не отчитывается.
    /// Не вызывай Progress.Report напрямую — используй ctx.Report(...).
    /// </summary>
    public IProgress<CommandProgress>? Progress { get; init; }

    /// <summary>
    /// Отчитаться о прогрессе. Никогда не бросает.
    /// Если Progress == null — no-op.
    /// </summary>
    public void Report(string stage, double? percent = null, string? message = null)
    {
        try
        {
            Progress?.Report(new CommandProgress
            {
                ExecutionId = ExecutionId,
                Stage = stage,
                Percent = percent,
                Message = message,
            });
        }
        catch
        {
            // Никогда не падаем из-за отчёта о прогрессе.
        }
    }
}