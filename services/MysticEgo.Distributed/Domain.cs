using Models.Data;
using System.Text.Json;
namespace MysticEgo.Distributed;

public enum NodeKind { Orchestra, Global, Local, Agent, AuditOrchestra }
public sealed class NodeOptions
{
    public string NodeId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public NodeKind Kind { get; set; }
    public string? GlobalUrl { get; set; }
    public string? OrchestraUrl { get; set; }
    public string? LocalUrl { get; set; }
    public string? PublicUrl { get; set; }
    public string DataPath { get; set; } = "data/node.db";
    public bool Managed { get; set; }
    public string? MachineFingerprint { get; set; }
    public string? GlobalId { get; set; }
    public string? LocalId { get; set; }
    public string? ClubAddress { get; set; }
    public string? ClubDescription { get; set; }
    public string? BrowserAuthUrl { get; set; }
    public string? ParentId { get; set; }
    public string AgentId { get; set; } = "";
    public string AuthUrl { get; set; } = "http://localhost:5400";
    public string IdentityId { get; set; } = "";
    public string IdentitySecret { get; set; } = "";
    public string DeploymentMode { get; set; } = "single-machine";
    public string? DeploymentDescription { get; set; }
    public bool AllowInsecureTestHttp { get; set; } = false;

}
public sealed record NodeAnnounce(string Id, string Name, string Kind, string? Url, string? ParentId, DateTimeOffset SeenAt, int AgentCount = 0);
[Entity("mysticego.record", version: 1)]
public sealed class RecordDocument
{
    [EntityKey] public Guid Id { get; set; }
    [EntityField(Required = true)] public string Collection { get; set; } = "";
    [EntityField(Required = true)] public string Name { get; set; } = "";
    public JsonElement Data { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public sealed record SaveRecord(string Name, JsonElement Data);
public sealed record BulkCommandRequest(Guid[] TargetIds,string CommandId,string? RawArgs,int TimeoutSeconds=30);
public sealed record CommandRequest(Guid TargetId, string CommandId, string? RawArgs, Dictionary<string, JsonElement>? NamedArgs, int TimeoutSeconds = 30, bool ConfirmedByHuman=false);
public sealed record AuditEntry(Guid Id, DateTimeOffset At, string Actor, string Action, string Target, bool Success, string? Detail);
public sealed record AgentSnapshot(Guid ClientId, string Host, string OS, DateTimeOffset SeenAt);
