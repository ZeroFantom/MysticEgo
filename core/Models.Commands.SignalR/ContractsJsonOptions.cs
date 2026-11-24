using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands.SignalR
{
    /// <summary>
    /// Единые JsonSerializerOptions для всего, что летает по сети.
    /// Подключает конвертеры идентификаторов.
    /// </summary>
    public static class ContractsJsonOptions
    {
        public static JsonSerializerOptions Default { get; } = Create();

        public static JsonSerializerOptions Create()
        {
            var opts = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
                WriteIndented = false,
            };

            opts.Converters.Add(new CommandIdJsonConverter());
            opts.Converters.Add(new PermissionIdJsonConverter());
            opts.Converters.Add(new ToolIdJsonConverter());
            opts.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

            return opts;
        }

        /// <summary>Для отладочного вывода — pretty-print.</summary>
        public static JsonSerializerOptions Pretty { get; } = CreatePretty();

        private static JsonSerializerOptions CreatePretty()
        {
            var opts = Create();
            opts.WriteIndented = true;
            return opts;
        }
    }
}