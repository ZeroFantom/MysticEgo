namespace Models.Commands;

/// <summary>
/// Прогресс выполнения команды. Отправляется клиентом на сервер
/// через IAgentHub.ReportProgress. Сервер ретранслирует в админку.
/// Опционален — большинство команд не отчитываются.
/// </summary>
public sealed record CommandProgress
{
    /// <summary>К какому выполнению относится.</summary>
    public required Guid ExecutionId { get; init; }

    /// <summary>Стадия: "start", "download", "extract", "verify", "done".</summary>
    public string Stage { get; init; } = "";

    /// <summary>Процент 0..100. null — неизвестно.</summary>
    public double? Percent { get; init; }

    /// <summary>Сообщение для отображения.</summary>
    public string? Message { get; init; }

    /// <summary>Оценка «сколько осталось». Опционально.</summary>
    public TimeSpan? EstimatedRemaining { get; init; }

    /// <summary>Момент отчёта (UTC).</summary>
    public DateTime ReportedAt { get; init; } = DateTime.UtcNow;
}