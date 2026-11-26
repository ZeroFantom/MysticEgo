using System.Collections.Concurrent;
using Models.Data;
namespace MysticEgo.Distributed;
public sealed class NodeState
{
    public ConcurrentDictionary<string, NodeAnnounce> Nodes { get; } = new();
    public ConcurrentQueue<AuditEntry> Audit { get; } = new();
    public ConcurrentDictionary<Guid, AgentSnapshot> AgentSnapshots { get; } = new();
    public AuditJournal Journal { get; }
    public NodeOptions Options { get; }
    public DynamicEntityStore Data { get; }
    public NodeState(NodeOptions options)
    {
        Options = options;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(options.DataPath))!);
        var path = Path.GetFullPath(options.DataPath);
        var connection = RelationalStorage.NodeConnection(path, options.DatabaseConnection, options.DatabaseConnectionFile);
        Journal = new AuditJournal(path + ".audit.db", RelationalStorage.IsPostgres(connection) ? connection : null);
        Data = new DynamicEntityStore(connection);
    }
    public void Log(string actor,string action,string target,bool success,string? detail=null)
    {
        var eventEntry = new AuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, actor, action, target, success, detail);
        try { Journal.Append(eventEntry); }
        catch (System.Data.Common.DbException error) { Console.Error.WriteLine("Audit write failed: " + error.GetType().Name); }
        Audit.Enqueue(eventEntry);
        while (Audit.Count > 1000) Audit.TryDequeue(out _);
    }
}
