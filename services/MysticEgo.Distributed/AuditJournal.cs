using Microsoft.Data.Sqlite;

namespace MysticEgo.Distributed;

public sealed record AuditPage(AuditEntry[] Items, long Total, int Page, int PageSize);

public sealed class AuditJournal
{
    private readonly string connectionString;

    public AuditJournal(string database)
    {
        connectionString = new SqliteConnectionStringBuilder { DataSource = database }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS action_journal (
                event_id TEXT NOT NULL, at TEXT NOT NULL, actor TEXT NOT NULL, action TEXT NOT NULL,
                target TEXT NOT NULL, success INTEGER NOT NULL, detail TEXT
            );
            CREATE INDEX IF NOT EXISTS action_journal_at ON action_journal (at DESC);
            CREATE INDEX IF NOT EXISTS action_journal_target ON action_journal (target, at DESC);
            """;
        command.ExecuteNonQuery();
    }

    public void Append(AuditEntry entry)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO action_journal VALUES ($id, $at, $actor, $action, $target, $success, $detail)";
        command.Parameters.AddWithValue("$id", entry.Id.ToString());
        command.Parameters.AddWithValue("$at", entry.At.UtcDateTime.ToString("O"));
        command.Parameters.AddWithValue("$actor", Limit(entry.Actor, 150));
        command.Parameters.AddWithValue("$action", Limit(entry.Action, 120));
        command.Parameters.AddWithValue("$target", Limit(entry.Target, 180));
        command.Parameters.AddWithValue("$detail", entry.Detail is null ? DBNull.Value : Limit(entry.Detail, 2000));
        command.Parameters.AddWithValue("$success", entry.Success);
        command.ExecuteNonQuery();
    }

    public async Task<AuditPage> Read(int page, int size, string? search, bool? success, string? target, CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 100_000);
        size = Math.Clamp(size, 5, 100);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        const string where = """
            WHERE ($query = '' OR instr(lower(actor || ' ' || action || ' ' || target || ' ' || COALESCE(detail, '')), lower($query)) > 0)
            AND ($success < 0 OR success = $success) AND ($target = '' OR target = $target)
            """;
        command.Parameters.AddWithValue("$query", Limit(search?.Trim() ?? "", 150));
        command.Parameters.AddWithValue("$success", success.HasValue ? (success.Value ? 1 : 0) : -1);
        command.Parameters.AddWithValue("$target", Limit(target ?? "", 180));
        command.CommandText = "SELECT COUNT(*) FROM action_journal " + where;
        var total = (long)(await command.ExecuteScalarAsync(ct) ?? 0L);
        command.CommandText = "SELECT event_id, at, actor, action, target, success, detail FROM action_journal " + where +
            " ORDER BY at DESC, rowid DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$offset", (page - 1) * size);
        using var rows = await command.ExecuteReaderAsync(ct);
        var items = new List<AuditEntry>();
        while (await rows.ReadAsync(ct))
            items.Add(new(Guid.Parse(rows.GetString(0)), DateTimeOffset.Parse(rows.GetString(1)), rows.GetString(2), rows.GetString(3),
                rows.GetString(4), rows.GetBoolean(5), rows.IsDBNull(6) ? null : rows.GetString(6)));
        return new(items.ToArray(), total, page, size);
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(connectionString);
        connection.Open();
        return connection;
    }

    private static string Limit(string text, int length) => text.Length <= length ? text : text[..length];
}
