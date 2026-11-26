using System.Data.Common;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Models.Data;

public static class RelationalStorage
{
    public static bool IsPostgres(string connectionString) =>
        connectionString.Contains("Host=", StringComparison.OrdinalIgnoreCase) ||
        connectionString.Contains("Server=", StringComparison.OrdinalIgnoreCase);

    public static DbConnection Open(string connectionString)
    {
        DbConnection connection = IsPostgres(connectionString)
            ? new NpgsqlConnection(connectionString) : new SqliteConnection(connectionString);
        try { connection.Open(); return connection; }
        catch { connection.Dispose(); throw; }
    }

    public static void AddValue(this DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public static string NodeConnection(string sqlitePath, string? configured, string? file)
    {
        if (!string.IsNullOrWhiteSpace(file))
        {
            if (!string.IsNullOrWhiteSpace(configured))
                throw new InvalidOperationException("Specify only one node database connection source.");
            if (!File.Exists(file)) throw new InvalidOperationException("Configured database connection file is missing.");
            configured = File.ReadAllText(file).Trim();
            if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("Empty database connection file.");
        }
        var marker = sqlitePath + ".postgres-configured";
        if (string.IsNullOrWhiteSpace(configured))
        {
            if (File.Exists(marker)) throw new InvalidOperationException("PostgreSQL was configured; refusing to fall back to an empty SQLite database.");
            return new SqliteConnectionStringBuilder { DataSource = sqlitePath }.ToString();
        }
        if (!IsPostgres(configured)) throw new InvalidOperationException("Only PostgreSQL is supported as an external node database.");
        if (new[] { sqlitePath, sqlitePath+"-wal", sqlitePath+".audit.db", sqlitePath+".audit.db-wal" }.Any(File.Exists))
            throw new InvalidOperationException("Existing SQLite node requires an explicit data migration before switching providers.");
        // Fail closed: a missing database must not silently create an unrelated SQLite node.
        using (var connection = Open(configured)) { }
        File.WriteAllText(marker, "PostgreSQL\n");
        return configured;
    }
}
