namespace Models.Commands.SignalR
{
    /// <summary>Обновление конфигурации, которое сервер присылает клиенту.</summary>
    public sealed record ConfigurationUpdate
    {
        /// <summary>Версия конфига (монотонно растёт).</summary>
        public required long Version { get; init; }

        /// <summary>Свободные key-value пары. Конкретика — на клиенте.</summary>
        public required IReadOnlyDictionary<string, string> Values { get; init; }

        /// <summary>Момент генерации.</summary>
        public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;
    }
}