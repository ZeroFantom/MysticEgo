using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands;

public sealed class ToolIdJsonConverter : JsonConverter<ToolId>
{
    public override ToolId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (string.IsNullOrWhiteSpace(raw))
            throw new JsonException("Идентификатор инструмента ToolId не может быть пустым.");

        return ToolId.FromName(raw);
    }

    public override void Write(
        Utf8JsonWriter writer, ToolId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}