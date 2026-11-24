namespace Models.Commands.SignalR
{
    /// <summary>Периодический отчёт от клиента серверу.</summary>
    public sealed record HeartbeatReport
    {
        /// <summary>Машиночитаемый ID клиента.</summary>
        public required Guid ClientId { get; init; }

        /// <summary>Момент отправки (UTC).</summary>
        public DateTime SentAt { get; init; } = DateTime.UtcNow;

        /// <summary>Сколько команд выполняется прямо сейчас.</summary>
        public int RunningCommands { get; init; }

        /// <summary>Аптайм процесса (секунды).</summary>
        public long UptimeSeconds { get; init; }

        /// <summary>Свободные метрики. Расширяемо.</summary>
        public IReadOnlyDictionary<string, object?>? Metrics { get; init; }
    }
}