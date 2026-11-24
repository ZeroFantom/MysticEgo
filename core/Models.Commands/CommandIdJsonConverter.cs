using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands;

/// <summary>
/// Сериализация идентификаторов как обычных строк.
///   CommandId "audio.list" → JSON "audio.list"
/// Никаких объектов, никаких {"value": "..."}.
/// </summary>
public sealed class CommandIdJsonConverter : JsonConverter<CommandId>
{
    public override CommandId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (string.IsNullOrWhiteSpace(raw))
            throw new JsonException("Идентификатор команды CommandId не может быть пустым.");

        return CommandId.FromName(raw);
    }

    public override void Write(
        Utf8JsonWriter writer, CommandId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}