using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace Models.Data;

/// <summary>Offline staging of node entities and audit into an empty PostgreSQL database.
/// Does not switch configuration, delete sources or migrate Auth. Unknown schemas fail closed.</summary>
public static class NodeDatabaseMigration
{
    public sealed record TableReport(string Name, long Rows, string Sha256);
    public sealed record Report(string BackupDirectory, IReadOnlyList<TableReport> Tables);
    private sealed record Column(string Name, string Type, bool Nullable = false);
    private sealed record Table(string Name, Column[] Columns, string Definition);

    private static Table Describe(string name) => name switch
    {
        "__entity_schemas" => new(name, [new("entity_id","TEXT"), new("version","INTEGER"), new("descriptor_json","TEXT")],
            "entity_id TEXT PRIMARY KEY NOT NULL, version INTEGER NOT NULL, descriptor_json TEXT NOT NULL"),
        "action_journal" => new(name, [new("event_id","TEXT"),new("at","TEXT"),new("actor","TEXT"),new("action","TEXT"),new("target","TEXT"),new("success","INTEGER"),new("detail","TEXT",true)],
            "event_id TEXT NOT NULL, at TEXT NOT NULL, actor TEXT NOT NULL, action TEXT NOT NULL, target TEXT NOT NULL, success INTEGER NOT NULL, detail TEXT"),
        _ when Regex.IsMatch(name, "^entity_[a-f0-9]{24}$") => new(name,
            [new("id","TEXT"),new("payload","TEXT"),new("schema_version","INTEGER"),new("updated_at","TEXT")],
            "id TEXT PRIMARY KEY NOT NULL, payload TEXT NOT NULL, schema_version INTEGER NOT NULL, updated_at TEXT NOT NULL"),
        _ => throw new InvalidOperationException("Unsupported source table; migration stopped.")
    };

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    private static SqliteConnection Source(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException("Source database is missing.");
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ConnectionString);
        connection.Open();
        return connection;
    }

    private static void VerifyIntegrity(SqliteConnection source)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandText = "PRAGMA integrity_check";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read() || reader.GetString(0) != "ok" || reader.Read())
            throw new InvalidOperationException("SQLite integrity check failed; do not migrate a damaged database.");
    }

    private static Table[] Inventory(SqliteConnection source, bool audit)
    {
        using var cmd = source.CreateCommand();
        cmd.CommandText = "SELECT name,type FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' AND type <> 'index'";
        var tables = new List<Table>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                if (reader.GetString(1) != "table") throw new InvalidOperationException("Unexpected source object.");
                var table = Describe(reader.GetString(0));
                if (audit != (table.Name == "action_journal")) throw new InvalidOperationException("Unexpected source storage layout.");
                tables.Add(table);
            }
        }
        foreach (var table in tables)
        {
            cmd.CommandText = "PRAGMA table_info(" + Quote(table.Name) + ")";
            using var columns = cmd.ExecuteReader();
            foreach (var expected in table.Columns)
                if (!columns.Read() || columns.GetString(1) != expected.Name || !columns.GetString(2).Equals(expected.Type, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Unexpected source columns.");
            if (columns.Read()) throw new InvalidOperationException("Unexpected extra source column.");
        }
        if (audit && tables.Count != 1) throw new InvalidOperationException("Audit journal table missing.");
        if (!audit && tables.Any(t=>t.Name.StartsWith("entity_")) && tables.All(t=>t.Name!="__entity_schemas"))
            throw new InvalidOperationException("Entity schema metadata missing.");
        return tables.OrderBy(t=>t.Name,StringComparer.Ordinal).ToArray();
    }

    private static void Backup(SqliteConnection source,string path)
    {
        using var target = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=path, Pooling=false }.ConnectionString);
        target.Open();
        source.BackupDatabase(target);
        VerifyIntegrity(target);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path,UnixFileMode.UserRead|UnixFileMode.UserWrite);
    }

    private static string Select(Table table,bool postgres)
    {
        var names=table.Columns.Select(c=>Quote(c.Name)).ToArray();
        var order=table.Columns.Select(c=>Quote(c.Name)+(c.Type=="TEXT"?(postgres?" COLLATE \"C\"":" COLLATE BINARY"):"")+(postgres?" NULLS FIRST":""));
        return "SELECT "+string.Join(",",names)+" FROM "+(postgres?"public.":"")+Quote(table.Name)+" ORDER BY "+string.Join(",",order);
    }

    private static object?[] Row(DbDataReader reader,Table table)
    {
        var values=new object?[table.Columns.Length];
        for(var i=0;i<values.Length;i++)
        {
            var column=table.Columns[i];
            if(reader.IsDBNull(i))
            {
                if(!column.Nullable)throw new InvalidOperationException("Unexpected null in source data.");
                continue;
            }
            var value=reader.GetValue(i);
            values[i]=column.Type=="INTEGER" ? value switch { int n=>n,long n=>checked((int)n),_=>throw new InvalidOperationException("Invalid integer value.") }
                : value is string text && !text.Contains('\0') ? text : throw new InvalidOperationException("Invalid text value.");
        }
        return values;
    }

    private static void Hash(IncrementalHash hash,object?[] values)
    {
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(values));
        hash.AppendData([10]);
    }

    private static TableReport Transfer(SqliteConnection source,NpgsqlConnection target,NpgsqlTransaction tx,Table table)
    {
        using var create = new NpgsqlCommand("CREATE TABLE public."+Quote(table.Name)+" ("+table.Definition+")",target,tx);
        create.ExecuteNonQuery();
        var parameters=Enumerable.Range(0,table.Columns.Length).Select(i=>"@p"+i).ToArray();
        using var insert=new NpgsqlCommand("INSERT INTO public."+Quote(table.Name)+" VALUES ("+string.Join(",",parameters)+")",target,tx);
        for(int i=0;i<table.Columns.Length;i++)
            insert.Parameters.Add("p"+i,table.Columns[i].Type=="INTEGER"?NpgsqlTypes.NpgsqlDbType.Integer:NpgsqlTypes.NpgsqlDbType.Text);
        using var command=source.CreateCommand();
        command.CommandText=Select(table,false);
        using var digest=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long count=0;
        using(var reader=command.ExecuteReader())
        {
            while(reader.Read())
            {
                var values=Row(reader,table);
                for(int i=0;i<values.Length;i++)insert.Parameters[i].Value=values[i]??DBNull.Value;
                insert.ExecuteNonQuery();
                Hash(digest,values);count++;
            }
        }
        var expected=Convert.ToHexString(digest.GetHashAndReset());
        using var verify=new NpgsqlCommand(Select(table,true),target,tx);
        long actual=0;
        using(var reader=verify.ExecuteReader())
            while(reader.Read()){Hash(digest,Row(reader,table));actual++;}
        if(actual!=count||Convert.ToHexString(digest.GetHashAndReset())!=expected)
            throw new InvalidOperationException("Destination verification failed; transaction rolled back.");
        return new(table.Name,count,expected);
    }

    private static void EmptyTarget(NpgsqlConnection target,NpgsqlTransaction tx)
    {
        using var cmd=new NpgsqlCommand("""
            SELECT pg_advisory_xact_lock(671021904);
            """,target,tx);
        cmd.ExecuteNonQuery();
        cmd.CommandText="""
            SELECT count(*) FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname='public';
            """;
        if(Convert.ToInt64(cmd.ExecuteScalar())!=0)throw new InvalidOperationException("Target public schema is not empty.");
    }

    /// <remarks>Caller MUST stop all source writers and reserve an empty destination exclusively for migration.</remarks>
    public static Report StageOffline(string nodePath,string auditPath,string postgres,string backupParent)
    {
        if(!RelationalStorage.IsPostgres(postgres))throw new InvalidOperationException("PostgreSQL target required.");
        if(Path.GetFullPath(nodePath)==Path.GetFullPath(auditPath))throw new InvalidOperationException("Source files must be distinct.");
        using var node=Source(nodePath);
        using var audit=Source(auditPath);
        VerifyIntegrity(node);VerifyIntegrity(audit);
        _ = Inventory(node,false);
        _ = Inventory(audit,true);
        using var target=new NpgsqlConnection(new NpgsqlConnectionStringBuilder(postgres){Timeout=5,CommandTimeout=60,Pooling=false}.ConnectionString);
        target.Open();
        using var tx=target.BeginTransaction();
        EmptyTarget(target,tx);
        if(!Directory.Exists(backupParent))throw new InvalidOperationException("Private backup parent must already exist.");
        var backup=Path.Combine(backupParent,"node-migration-"+Guid.NewGuid().ToString("N"));
        if(OperatingSystem.IsWindows())Directory.CreateDirectory(backup);
        else Directory.CreateDirectory(backup,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        Backup(node,Path.Combine(backup,"node.db"));
        Backup(audit,Path.Combine(backup,"node.db.audit.db"));
        // Transfer from immutable backup snapshots, never from live paths.
        using var nodeCopy=Source(Path.Combine(backup,"node.db"));
        using var auditCopy=Source(Path.Combine(backup,"node.db.audit.db"));
        var reports=Inventory(nodeCopy,false).Select(t=>Transfer(nodeCopy,target,tx,t))
            .Concat(Inventory(auditCopy,true).Select(t=>Transfer(auditCopy,target,tx,t))).ToArray();
        using(var indexes=new NpgsqlCommand("""
            CREATE INDEX action_journal_at ON public.action_journal (at DESC);
            CREATE INDEX action_journal_target ON public.action_journal (target, at DESC);
            """,target,tx))indexes.ExecuteNonQuery();
        // Commit only after verifying every transferred value. Original files remain untouched.
        tx.Commit();
        return new(backup,reports);
    }
}
