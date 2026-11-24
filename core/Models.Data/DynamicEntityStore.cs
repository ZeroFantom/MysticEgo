using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Models.Data;

/// <summary>
/// Динамическое хранилище JSON-сущностей в SQLite. Для каждого идентификатора сущности используется отдельная таблица.
/// Политика миграций: только добавление полей с явной версией; потенциально разрушительные изменения запрещены.
/// Это хранилище локального узла; репликация и распределённая координация остаются ответственностью сервисов.
/// </summary>
public sealed class DynamicEntityStore
{
    private readonly string _connectionString;
    private readonly JsonSerializerOptions _json;

    public DynamicEntityStore(string connectionString, JsonSerializerOptions? json = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _json = json ?? new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    private static string Table(string id)
    {
        var hex = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id))).ToLowerInvariant();
        return "entity_" + hex[..24]; // Не подставляем идентификаторы пользователя непосредственно в SQL.
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    public async Task EnsureSchemaAsync<T>(CancellationToken ct = default) where T : class
    {
        var descriptor = EntityMetadata.Describe<T>();
        using var connection = Open();
        using var transaction = connection.BeginTransaction();
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = "CREATE TABLE IF NOT EXISTS __entity_schemas (entity_id TEXT PRIMARY KEY, version INTEGER NOT NULL, descriptor_json TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        var previous = await ReadSchemaAsync(connection, transaction, descriptor.Id, ct).ConfigureAwait(false);
        if (previous is not null) ValidateUpgrade(previous.Value.Version, previous.Value.Json, descriptor);
        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = $"CREATE TABLE IF NOT EXISTS {Table(descriptor.Id)} (id TEXT PRIMARY KEY, payload TEXT NOT NULL, schema_version INTEGER NOT NULL, updated_at TEXT NOT NULL);";
            await create.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        using (var upsert = connection.CreateCommand())
        {
            upsert.Transaction = transaction;
            upsert.CommandText = "INSERT INTO __entity_schemas(entity_id, version, descriptor_json) VALUES ($id, $ver, $json) ON CONFLICT(entity_id) DO UPDATE SET version=excluded.version, descriptor_json=excluded.descriptor_json;";
            upsert.Parameters.AddWithValue("$id", descriptor.Id);
            upsert.Parameters.AddWithValue("$ver", descriptor.Version);
            upsert.Parameters.AddWithValue("$json", descriptor.SchemaJson);
            await upsert.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
        transaction.Commit();
    }

    private static async Task<(int Version, string Json)?> ReadSchemaAsync(SqliteConnection c, SqliteTransaction tx, string id, CancellationToken ct)
    {
        using var command = c.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT version, descriptor_json FROM __entity_schemas WHERE entity_id=$id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false)) return null;
        return (reader.GetInt32(0), reader.GetString(1));
    }

    private static void ValidateUpgrade(int previousVersion, string previousJson, EntityDescriptor target)
    {
        if (target.Version < previousVersion)
            throw new InvalidOperationException("Понижение версии схемы сущности запрещено.");
        using var oldDoc = JsonDocument.Parse(previousJson);
        using var newDoc = JsonDocument.Parse(target.SchemaJson);
        var oldFields = oldDoc.RootElement.GetProperty("fields").EnumerateArray()
            .ToDictionary(x => x.GetProperty("Name").GetString()!, x => x);
        var newFields = newDoc.RootElement.GetProperty("fields").EnumerateArray()
            .ToDictionary(x => x.GetProperty("Name").GetString()!, x => x);
        foreach (var (name, old) in oldFields)
        {
            if (!newFields.TryGetValue(name, out var updated) ||
                old.GetProperty("Type").GetString() != updated.GetProperty("Type").GetString() ||
                old.GetProperty("Key").GetBoolean() != updated.GetProperty("Key").GetBoolean())
                throw new InvalidOperationException($"Удаление или несовместимое изменение поля '{name}' требует явной миграции.");
            if (!old.GetProperty("Required").GetBoolean() && updated.GetProperty("Required").GetBoolean())
                throw new InvalidOperationException($"Изменение поля '{name}' на обязательное требует миграции данных.");
        }
        if (newFields.Values.Any(f => f.GetProperty("Required").GetBoolean() &&
             !oldFields.ContainsKey(f.GetProperty("Name").GetString()!)))
            throw new InvalidOperationException("Новые обязательные поля требуют явной миграции данных.");
        if (target.Version == previousVersion && previousJson != target.SchemaJson)
            throw new InvalidOperationException("Изменение схемы требует увеличения версии.");
    }

    public async Task PutAsync<T>(T value, CancellationToken ct = default) where T : class
    {
        ArgumentNullException.ThrowIfNull(value);
        var d = EntityMetadata.Describe<T>();
        await EnsureSchemaAsync<T>(ct).ConfigureAwait(false);
        var property = typeof(T).GetProperty(d.KeyName)!;
        var key = (Guid)property.GetValue(value)!;
        if (key == Guid.Empty) throw new ArgumentException("Ключ сущности не может быть пустым.", nameof(value));
        foreach (var field in d.Fields.Where(f => f.Required))
            if (typeof(T).GetProperty(field.Name)!.GetValue(value) is null)
                throw new ArgumentException($"Обязательное поле '{field.Name}' не заполнено.", nameof(value));
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"INSERT INTO {Table(d.Id)} (id,payload,schema_version,updated_at) VALUES ($id,$json,$ver,$time) ON CONFLICT(id) DO UPDATE SET payload=excluded.payload, schema_version=excluded.schema_version, updated_at=excluded.updated_at;";
        command.Parameters.AddWithValue("$id", key.ToString("D"));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(value, _json));
        command.Parameters.AddWithValue("$ver", d.Version);
        command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<T?> GetAsync<T>(Guid key, CancellationToken ct = default) where T : class
    {
        var d = EntityMetadata.Describe<T>();
        await EnsureSchemaAsync<T>(ct).ConfigureAwait(false);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {Table(d.Id)} WHERE id=$id;";
        command.Parameters.AddWithValue("$id", key.ToString("D"));
        var payload = (string?)await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return payload is null ? null : JsonSerializer.Deserialize<T>(payload, _json);
    }

    public async Task<bool> DeleteAsync<T>(Guid key, CancellationToken ct = default) where T : class
    {
        var d = EntityMetadata.Describe<T>();
        await EnsureSchemaAsync<T>(ct).ConfigureAwait(false);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM {Table(d.Id)} WHERE id=$id;";
        command.Parameters.AddWithValue("$id", key.ToString("D"));
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    /// <summary>Bounded listing for node-local management records; filtering belongs to the application.</summary>
    public async Task<IReadOnlyList<T>> ListAsync<T>(int limit = 500, CancellationToken ct = default) where T : class
    {
        if (limit < 1 || limit > 5000) throw new ArgumentOutOfRangeException(nameof(limit));
        var d = EntityMetadata.Describe<T>();
        await EnsureSchemaAsync<T>(ct).ConfigureAwait(false);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT payload FROM {Table(d.Id)} ORDER BY updated_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", limit);
        using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var results = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            var value = JsonSerializer.Deserialize<T>(reader.GetString(0), _json);
            if (value is not null) results.Add(value);
        }
        return results;
    }

}
