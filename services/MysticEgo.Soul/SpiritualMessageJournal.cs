using Microsoft.Data.Sqlite;

namespace MysticEgo.Soul;

public enum SpiritualJournalStatus { Pending, Committed, Rejected }
internal sealed record JournalEntry(Guid Owner, Guid Request, SpiritualMessage Message, SpiritualJournalStatus Status);

/// <summary>Local durable queue and inbox. One authoritative gateway process must own the file;
/// not distributed coordination or network delivery. Use a dedicated protected data directory.
/// Account/request identifiers stay in the journal, not the in-world envelope.</summary>
public sealed class SpiritualMessageJournal
{
    private readonly string connectionString;
    public SpiritualMessageJournal(string file)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(file);
        var full = Path.GetFullPath(file);
        connectionString = new SqliteConnectionStringBuilder { DataSource = full, Pooling = false, DefaultTimeout = 5 }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS spiritual_messages (
                seq INTEGER PRIMARY KEY AUTOINCREMENT,
                owner TEXT NOT NULL, request TEXT NOT NULL, id TEXT NOT NULL UNIQUE,
                sender TEXT NOT NULL, recipient TEXT NOT NULL, world TEXT NOT NULL,
                text TEXT NOT NULL, accepted TEXT NOT NULL, status INTEGER NOT NULL CHECK(status BETWEEN 0 AND 2),
                UNIQUE(owner,request)
            );
            CREATE INDEX IF NOT EXISTS spiritual_messages_pending ON spiritual_messages(status,seq);
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }

    internal SpiritualSendResult Enqueue(Guid owner, SpiritualSendRequest request, SpiritualMessage proposed, int maxPending, int maxRecords)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT owner,request,id,sender,recipient,world,text,accepted,status FROM spiritual_messages WHERE owner=$owner AND request=$request";
        cmd.Parameters.AddWithValue("$owner",owner.ToString("D"));
        cmd.Parameters.AddWithValue("$request",request.RequestId.ToString("D"));
        JournalEntry? existing;
        using(var reader = cmd.ExecuteReader()) existing = reader.Read() ? Read(reader) : null;
        if(existing is not null)
        {
            var same = existing.Message.SenderId == proposed.SenderId && existing.Message.RecipientId == proposed.RecipientId &&
                existing.Message.WorldId == proposed.WorldId && existing.Message.Text == proposed.Text;
            return same && existing.Status != SpiritualJournalStatus.Rejected
                ? new(SpiritualSendStatus.Accepted,existing.Message.Id) : new(SpiritualSendStatus.Unavailable);
        }
        cmd.Parameters.Clear();
        cmd.CommandText="SELECT COUNT(*), COALESCE(SUM(CASE WHEN status=0 THEN 1 ELSE 0 END),0) FROM spiritual_messages";
        using(var reader=cmd.ExecuteReader())
        {
            reader.Read();
            if(reader.GetInt64(0)>=maxRecords||reader.GetInt64(1)>=maxPending)return new(SpiritualSendStatus.Unavailable);
        }
        cmd.CommandText="INSERT INTO spiritual_messages(owner,request,id,sender,recipient,world,text,accepted,status) VALUES($owner,$request,$id,$sender,$recipient,$world,$text,$accepted,0)";
        cmd.Parameters.AddWithValue("$owner",owner.ToString("D"));cmd.Parameters.AddWithValue("$request",request.RequestId.ToString("D"));
        cmd.Parameters.AddWithValue("$id",proposed.Id.ToString("D"));cmd.Parameters.AddWithValue("$sender",proposed.SenderId);
        cmd.Parameters.AddWithValue("$recipient",proposed.RecipientId);cmd.Parameters.AddWithValue("$world",proposed.WorldId);
        cmd.Parameters.AddWithValue("$text",proposed.Text);cmd.Parameters.AddWithValue("$accepted",proposed.AcceptedUtc.ToString("O"));
        cmd.ExecuteNonQuery();tx.Commit();
        return new(SpiritualSendStatus.Accepted,proposed.Id);
    }

    internal JournalEntry? Next()
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT owner,request,id,sender,recipient,world,text,accepted,status FROM spiritual_messages WHERE status=0 ORDER BY seq LIMIT 1";
        using var reader=cmd.ExecuteReader();return reader.Read()?Read(reader):null;
    }

    internal bool Finish(Guid id,bool allowed)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="UPDATE spiritual_messages SET status=$status WHERE id=$id AND status=0";
        cmd.Parameters.AddWithValue("$status",allowed?1:2);cmd.Parameters.AddWithValue("$id",id.ToString("D"));
        return cmd.ExecuteNonQuery()==1;
    }

    internal SpiritualMessage? Committed(Guid id)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT owner,request,id,sender,recipient,world,text,accepted,status FROM spiritual_messages WHERE id=$id AND status=1";
        cmd.Parameters.AddWithValue("$id",id.ToString("D"));
        using var reader=cmd.ExecuteReader();return reader.Read()?Read(reader).Message:null;
    }

    private static JournalEntry Read(SqliteDataReader row) => new(Guid.Parse(row.GetString(0)),Guid.Parse(row.GetString(1)),
        new(Guid.Parse(row.GetString(2)),row.GetString(3),row.GetString(4),row.GetString(5),row.GetString(6),
            DateTimeOffset.ParseExact(row.GetString(7),"O",System.Globalization.CultureInfo.InvariantCulture)),
        (SpiritualJournalStatus)row.GetInt32(8));
}
