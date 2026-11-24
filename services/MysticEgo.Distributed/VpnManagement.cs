using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Models.Data;

namespace MysticEgo.Distributed;

// Prototype VPN control plane. Provisioning and 3x-ui access must be handled by a separate
// trusted worker before users can receive an approved subscription. Never send panel secrets to browsers.
[Entity("mysticego.vpn.request", version:1)]
public sealed class VpnRequest
{
    [EntityKey] public Guid Id {get;set;}
    [EntityField(Required=true)] public string UserId {get;set;}="";
    public string Reason {get;set;}="";
    public string Status {get;set;}="pending"; // pending / approved / rejected / revoked
    public string Region {get;set;}="auto";
    public string PanelId {get;set;}="";
    // Stable client identifier in the registered 3x-ui panel. Required for managed revocation.
    public string ClientEmail {get;set;}="";
    public string EncryptedSubscription {get;set;}="";
    public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt {get;set;}=DateTimeOffset.UtcNow;
}
[Entity("mysticego.vpn.panel",version:1)]
public sealed class VpnPanel
{
    [EntityKey]public Guid Id {get;set;}
    public string Label {get;set;}="";
    public string BaseUrl {get;set;}="";
    public string Region {get;set;}="";
    public string Status {get;set;}="configured";
    public DateTimeOffset RegisteredAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed record VpnApply(string Region,string Reason);
public sealed record VpnDecision(string Status,string? SubscriptionUrl,string? PanelId,string? ClientEmail=null);
public sealed record VpnPanelCreate(string Label,string BaseUrl,string Region);
public sealed record VpnDeployNode(string Host,string Region,string? GlobalUrl,string? OrchestraUrl);
public sealed record VpnDeployDraft(VpnDeployNode[] Nodes);

