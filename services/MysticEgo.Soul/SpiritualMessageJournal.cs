using Microsoft.Data.Sqlite;

namespace MysticEgo.Soul;

public enum SpiritualJournalStatus { Pending, Committed, Rejected }
internal sealed record JournalEntry(Guid Owner, Guid Request, SpiritualMessage Message, SpiritualJournalStatus Status, SpiritualParticipantKind RecipientKind, Guid? RecipientOwner);

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
        // Additive upgrade; old records without target identity cannot be safely delivered.
        var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        cmd.CommandText="PRAGMA table_info(spiritual_messages)";
        using(var rows=cmd.ExecuteReader())while(rows.Read())columns.Add(rows.GetString(1));
        if(!columns.Contains("recipient_kind"))
        {cmd.CommandText="ALTER TABLE spiritual_messages ADD COLUMN recipient_kind INTEGER NOT NULL DEFAULT 0";cmd.ExecuteNonQuery();}
        if(!columns.Contains("recipient_owner"))
        {cmd.CommandText="ALTER TABLE spiritual_messages ADD COLUMN recipient_owner TEXT NULL";cmd.ExecuteNonQuery();}
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS spiritual_npc_replies (
                parent TEXT PRIMARY KEY REFERENCES spiritual_messages(id) ON DELETE CASCADE,
                id TEXT NOT NULL UNIQUE, text TEXT NOT NULL, created TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }

    internal SpiritualSendResult Enqueue(Guid owner, SpiritualSendRequest request, SpiritualMessage proposed, SpiritualParticipantKind kind, Guid? recipientOwner, int maxPending, int maxRecords)
    {
        using var db = Open();
        using var tx = db.BeginTransaction();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT owner,request,id,sender,recipient,world,text,accepted,status,recipient_kind,recipient_owner FROM spiritual_messages WHERE owner=$owner AND request=$request";
        cmd.Parameters.AddWithValue("$owner",owner.ToString("D"));
        cmd.Parameters.AddWithValue("$request",request.RequestId.ToString("D"));
        JournalEntry? existing;
        using(var reader = cmd.ExecuteReader()) existing = reader.Read() ? Read(reader) : null;
        if(existing is not null)
        {
            var same = existing.Message.SenderId == proposed.SenderId && existing.Message.RecipientId == proposed.RecipientId &&
                existing.Message.WorldId == proposed.WorldId && existing.Message.Text == proposed.Text &&
                existing.RecipientKind==kind && existing.RecipientOwner==recipientOwner;
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
        cmd.CommandText="INSERT INTO spiritual_messages(owner,request,id,sender,recipient,world,text,accepted,status,recipient_kind,recipient_owner) VALUES($owner,$request,$id,$sender,$recipient,$world,$text,$accepted,0,$kind,$targetOwner)";
        cmd.Parameters.AddWithValue("$owner",owner.ToString("D"));cmd.Parameters.AddWithValue("$request",request.RequestId.ToString("D"));
        cmd.Parameters.AddWithValue("$id",proposed.Id.ToString("D"));cmd.Parameters.AddWithValue("$sender",proposed.SenderId);
        cmd.Parameters.AddWithValue("$recipient",proposed.RecipientId);cmd.Parameters.AddWithValue("$world",proposed.WorldId);
        cmd.Parameters.AddWithValue("$text",proposed.Text);cmd.Parameters.AddWithValue("$accepted",proposed.AcceptedUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$kind",(int)kind);
        cmd.Parameters.AddWithValue("$targetOwner",recipientOwner is Guid target?target.ToString("D"):(object)DBNull.Value);
        cmd.ExecuteNonQuery();tx.Commit();
        return new(SpiritualSendStatus.Accepted,proposed.Id);
    }

    internal JournalEntry? Next()
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT owner,request,id,sender,recipient,world,text,accepted,status,recipient_kind,recipient_owner FROM spiritual_messages WHERE status=0 ORDER BY seq LIMIT 1";
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
        cmd.CommandText="SELECT owner,request,id,sender,recipient,world,text,accepted,status,recipient_kind,recipient_owner FROM spiritual_messages WHERE id=$id AND status=1";
        cmd.Parameters.AddWithValue("$id",id.ToString("D"));
        using var reader=cmd.ExecuteReader();return reader.Read()?Read(reader).Message:null;
    }

    internal JournalEntry? CommittedEntry(Guid id)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT owner,request,id,sender,recipient,world,text,accepted,status,recipient_kind,recipient_owner FROM spiritual_messages WHERE id=$id AND status=1";
        cmd.Parameters.AddWithValue("$id",id.ToString("D"));
        using var rows=cmd.ExecuteReader();return rows.Read()?Read(rows):null;
    }

    internal bool HasReply(Guid parent)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT EXISTS(SELECT 1 FROM spiritual_npc_replies WHERE parent=$parent)";
        cmd.Parameters.AddWithValue("$parent",parent.ToString("D"));return Convert.ToInt64(cmd.ExecuteScalar())==1;
    }

    internal bool SaveReply(Guid parent,string text,DateTimeOffset now)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="INSERT INTO spiritual_npc_replies(parent,id,text,created) SELECT id,$id,$text,$created FROM spiritual_messages WHERE id=$parent AND status=1 AND recipient_kind=2 ON CONFLICT(parent) DO NOTHING";
        cmd.Parameters.AddWithValue("$parent",parent.ToString("D"));cmd.Parameters.AddWithValue("$id",Guid.NewGuid().ToString("D"));
        cmd.Parameters.AddWithValue("$text",text);cmd.Parameters.AddWithValue("$created",now.ToString("O"));
        return cmd.ExecuteNonQuery()==1;
    }

    // Exact account, incarnation, world and NPC; only prior completed exchanges.
    // Never retrieve personal memories, other players, other NPCs or later concurrent turns.
    internal IReadOnlyList<SpiritualNpcMemory> NpcMemory(JournalEntry current,int limit)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT m.text,r.text FROM spiritual_messages m
            JOIN spiritual_npc_replies r ON r.parent=m.id
            WHERE m.status=1 AND m.recipient_kind=2 AND m.owner=$owner
              AND m.sender=$sender AND m.recipient=$npc AND m.world=$world
              AND m.seq<(SELECT seq FROM spiritual_messages WHERE id=$current)
              AND r.created<=$accepted
            ORDER BY m.seq DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$owner",current.Owner.ToString("D"));
        cmd.Parameters.AddWithValue("$sender",current.Message.SenderId);
        cmd.Parameters.AddWithValue("$npc",current.Message.RecipientId);
        cmd.Parameters.AddWithValue("$world",current.Message.WorldId);
        cmd.Parameters.AddWithValue("$current",current.Message.Id.ToString("D"));
        cmd.Parameters.AddWithValue("$accepted",current.Message.AcceptedUtc.ToUniversalTime().ToString("O"));
        cmd.Parameters.AddWithValue("$limit",limit);
        using var rows=cmd.ExecuteReader();var result=new List<SpiritualNpcMemory>();
        static string Bounded(string text)=>text.Length<=400?text:text[..400];
        while(rows.Read())result.Add(new(Bounded(rows.GetString(0)),Bounded(rows.GetString(1))));
        result.Reverse();
        return result.AsReadOnly();
    }

    // Only committed messages for the authenticated owner and current owned embodiment.
    // No caller-supplied peer or global message lookup. Includes replies even after movement.
    internal IReadOnlyList<SpiritualHistoryItem> History(Guid account,string entity,int limit)
    {
        using var db=Open();using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT m.id,m.text,m.accepted,m.sender,m.recipient,r.id,r.text,r.created
            FROM spiritual_messages m LEFT JOIN spiritual_npc_replies r ON r.parent=m.id
            WHERE m.status=1 AND m.recipient_kind IN (1,2) AND
                ((m.owner=$account AND m.sender=$entity) OR
                 (m.recipient_kind=1 AND m.recipient_owner=$account AND m.recipient=$entity))
            ORDER BY m.seq DESC LIMIT $limit
            """;
        cmd.Parameters.AddWithValue("$account",account.ToString("D"));cmd.Parameters.AddWithValue("$entity",entity);
        cmd.Parameters.AddWithValue("$limit",limit);
        using var rows=cmd.ExecuteReader();var result=new List<SpiritualHistoryItem>();
        while(rows.Read())
        {
            result.Add(new(Guid.Parse(rows.GetString(0)),rows.GetString(3)==entity?"outgoing":"incoming",rows.GetString(1),
                DateTimeOffset.ParseExact(rows.GetString(2),"O",System.Globalization.CultureInfo.InvariantCulture)));
            if(!rows.IsDBNull(5))result.Add(new(Guid.Parse(rows.GetString(5)),"incoming",rows.GetString(6),
                DateTimeOffset.ParseExact(rows.GetString(7),"O",System.Globalization.CultureInfo.InvariantCulture)));
        }
        return result.OrderByDescending(x=>x.CreatedUtc).Take(limit).ToArray();
    }

    private static JournalEntry Read(SqliteDataReader row) => new(Guid.Parse(row.GetString(0)),Guid.Parse(row.GetString(1)),
        new(Guid.Parse(row.GetString(2)),row.GetString(3),row.GetString(4),row.GetString(5),row.GetString(6),
            DateTimeOffset.ParseExact(row.GetString(7),"O",System.Globalization.CultureInfo.InvariantCulture)),
        (SpiritualJournalStatus)row.GetInt32(8),(SpiritualParticipantKind)row.GetInt32(9),row.IsDBNull(10)?null:Guid.Parse(row.GetString(10)));
}
