using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands;

public sealed class PermissionIdJsonConverter : JsonConverter<PermissionId>
{
    public override PermissionId Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString();

        if (string.IsNullOrWhiteSpace(raw))
            throw new JsonException("Идентификатор разрешения PermissionId не может быть пустым.");

        return PermissionId.FromName(raw);
    }

    public override void Write(
        Utf8JsonWriter writer, PermissionId value, JsonSerializerOptions options)
        => writer.WriteStringValue(value.Value);
}