using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace MysticEgo.Soul;

public sealed record WorldAuthorityRevision(long Revision,WorldPresenceFeed Feed);

/// <summary>Durable single-world authority contract. Only a trusted simulation writer may construct
/// and publish; no HTTP/MCP or player mutation endpoint. Full snapshots are authoritative: an absent
/// entity is retired forever, not temporarily unloaded. Use a private local SQLite database.</summary>
public sealed class WorldAuthorityStore
{
    private readonly string path;
    private readonly TimeProvider clock;
    private const int MaxIdentities=65536;
    public WorldAuthorityStore(string path,TimeProvider clock)
    {
        if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Absolute authority path required.");
        this.path=path;this.clock=clock;
        using var db=Open(path,SqliteOpenMode.ReadWriteCreate);
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS world_authority (
                singleton INTEGER PRIMARY KEY CHECK(singleton=1),
                revision INTEGER NOT NULL CHECK(revision>=0),
                payload BLOB NULL
            );
            INSERT INTO world_authority(singleton,revision,payload) VALUES(1,0,NULL) ON CONFLICT(singleton) DO NOTHING;
            CREATE TABLE IF NOT EXISTS world_entity_lifetimes (
                entity TEXT PRIMARY KEY,
                world TEXT NOT NULL,
                kind INTEGER NOT NULL,
                owner TEXT NULL,
                retired INTEGER NOT NULL CHECK(retired IN (0,1))
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static SqliteConnection Open(string path,SqliteOpenMode mode)
    {
        var db=new SqliteConnection(new SqliteConnectionStringBuilder{
            DataSource=path,Mode=mode,Pooling=false,DefaultTimeout=3}.ToString());
        db.Open();return db;
    }

    /// <summary>Read-only; never creates or repairs storage. Internal full state is not a client DTO.</summary>
    public static WorldAuthorityRevision? ReadCurrent(string path)
    {
        if(!Path.IsPathFullyQualified(path))throw new ArgumentException("Absolute authority path required.");
        using var db=Open(path,SqliteOpenMode.ReadOnly);
        using var cmd=db.CreateCommand();
        cmd.CommandText="SELECT revision,length(payload),payload FROM world_authority WHERE singleton=1";
        using var rows=cmd.ExecuteReader();
        if(!rows.Read()||rows.GetInt64(0)<=0||rows.IsDBNull(1)||rows.GetInt64(1) is <=0 or >WorldPresenceFeedReader.MaxBytes)return null;
        var payload=(byte[])rows.GetValue(2);
        var feed=JsonSerializer.Deserialize<WorldPresenceFeed>(payload,WorldPresenceFeedReader.Json);
        return feed is null?null:new(rows.GetInt64(0),feed);
    }

    private sealed record Lifetime(string World,int Kind,string? Owner,bool Retired);

    /// <summary>Atomic compare-and-swap. The simulation supplies complete placements, active
    /// embodiments, grants, friendships and approved NPC profiles together. A stale writer
    /// cannot overwrite movement or revive permissions. No client-supplied clock.</summary>
    public bool TryPublish(long expected,SpiritualWorldSnapshot snapshot,out long revision)
    {
        revision=expected;
        if(expected<0||expected==long.MaxValue||snapshot is null)return false;
        // Serialize and deserialize before validation, so caller mutations cannot alter the
        // exact payload committed after validation.
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new WorldPresenceFeed(clock.GetUtcNow().ToUniversalTime(),snapshot),
            WorldPresenceFeedReader.Json);
        if(bytes.Length>WorldPresenceFeedReader.MaxBytes)return false;
        var copy=JsonSerializer.Deserialize<WorldPresenceFeed>(bytes,WorldPresenceFeedReader.Json);
        if(copy is null||WorldPresenceFeedReader.Validate(copy.Snapshot,clock) is null)return false;
        using var db=Open(path,SqliteOpenMode.ReadWrite);
        using var tx=db.BeginTransaction(deferred:false);
        using var cmd=db.CreateCommand();cmd.Transaction=tx;
        cmd.CommandText="SELECT revision FROM world_authority WHERE singleton=1";
        revision=Convert.ToInt64(cmd.ExecuteScalar());
        if(revision!=expected)return false;
        var lifetimes=new Dictionary<string,Lifetime>(StringComparer.Ordinal);
        cmd.CommandText="SELECT entity,world,kind,owner,retired FROM world_entity_lifetimes LIMIT 65537";
        using(var rows=cmd.ExecuteReader())
            while(rows.Read())lifetimes.Add(rows.GetString(0),new(rows.GetString(1),rows.GetInt32(2),
                rows.IsDBNull(3)?null:rows.GetString(3),rows.GetInt32(4)==1));
        if(lifetimes.Count>MaxIdentities)return false;
        var present=new HashSet<string>(StringComparer.Ordinal);
        foreach(var item in copy.Snapshot.Placements)
        {
            var p=item.Presence;present.Add(p.EntityId);
            var next=new Lifetime(p.WorldId,(int)p.Kind,item.AccountId?.ToString("D"),false);
            if(lifetimes.TryGetValue(p.EntityId,out var old))
            {
                if(old!=next)return false; // no reassignment, world transfer or retired identity reuse
            }
            else
            {
                if(lifetimes.Count>=MaxIdentities)return false;
                lifetimes.Add(p.EntityId,next);
                cmd.Parameters.Clear();
                cmd.CommandText="INSERT INTO world_entity_lifetimes(entity,world,kind,owner,retired) VALUES($id,$world,$kind,$owner,0)";
                cmd.Parameters.AddWithValue("$id",p.EntityId);cmd.Parameters.AddWithValue("$world",next.World);
                cmd.Parameters.AddWithValue("$kind",next.Kind);cmd.Parameters.AddWithValue("$owner",(object?)next.Owner??DBNull.Value);
                cmd.ExecuteNonQuery();
            }
        }
        foreach(var (id,old) in lifetimes)
        {
            if(old.Retired||present.Contains(id))continue;
            cmd.Parameters.Clear();cmd.CommandText="UPDATE world_entity_lifetimes SET retired=1 WHERE entity=$id";
            cmd.Parameters.AddWithValue("$id",id);cmd.ExecuteNonQuery();
        }
        // Do not refresh a snapshot that spent too long waiting on the storage lock.
        var now=clock.GetUtcNow();
        if(copy.PublishedUtc<=now.AddSeconds(-15)||copy.PublishedUtc>now.AddSeconds(2))return false;
        cmd.Parameters.Clear();
        cmd.CommandText="UPDATE world_authority SET revision=$next,payload=$payload WHERE singleton=1 AND revision=$expected";
        cmd.Parameters.AddWithValue("$next",expected+1);cmd.Parameters.AddWithValue("$payload",bytes);
        cmd.Parameters.AddWithValue("$expected",expected);
        if(cmd.ExecuteNonQuery()!=1)return false;
        tx.Commit();revision=expected+1;return true;
    }
}
