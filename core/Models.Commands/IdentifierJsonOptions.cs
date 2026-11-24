using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands;

/// <summary>
/// Готовый набор опций для сериализации чего угодно, где встречаются ID.
/// </summary>
public static class IdentifierJsonOptions
{
    public static JsonSerializerOptions Default { get; } = Create();

    public static JsonSerializerOptions Create()
    {
        var opts = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };

        opts.Converters.Add(new CommandIdJsonConverter());
        opts.Converters.Add(new PermissionIdJsonConverter());
        opts.Converters.Add(new ToolIdJsonConverter());

        return opts;
    }
}