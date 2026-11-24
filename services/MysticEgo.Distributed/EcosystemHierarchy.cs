using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Models.Data;
using Models.Commands;
using Microsoft.AspNetCore.Routing;

namespace MysticEgo.Distributed;

/// <summary>Иерархия узлов — НЕ замена проверке разрешений JWT и ACL ресурсов.</summary>
public static class EcosystemHierarchy
{
    public static readonly IReadOnlyDictionary<NodeKind,int> Levels = new Dictionary<NodeKind,int>
    {
        [NodeKind.Orchestra]=50, [NodeKind.Global]=40, [NodeKind.Local]=30,
        [NodeKind.Agent]=20, [NodeKind.AuditOrchestra]=20
    };
    public static bool IsDeveloper(ClaimsPrincipal u) => u.Identity?.IsAuthenticated==true
        && u.FindFirst("token_use")?.Value!="device"
        && (u.IsInRole("system.developer") || u.HasClaim("permission","system.bypass"));
    public static object Description(NodeKind k) => new {
        kind=k.ToString(), tier=Levels[k],
        label=k switch {NodeKind.Orchestra=>"Orchestra · центр разработки",NodeKind.AuditOrchestra=>"AuditOrchestra · домашний узел",NodeKind.Global=>"Global · инфраструктура",NodeKind.Local=>"Local · площадка",_=>"Agent · клубный компьютер"},
        role=k switch {NodeKind.Orchestra=>"development",NodeKind.AuditOrchestra=>"home",NodeKind.Global=>"cloud",NodeKind.Local=>"site",_=>"station"}
    };
}

/// <summary>Метаданные игровых аккаунтов, не выдающие игровых предметов или характеристик без игрового сервера.</summary>
[Entity("mysticego.ether.account",version:1)]
public sealed class EtherAccount
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string UserId {get;set;}="";
    public string Nickname {get;set;}="";
    public string GameAccountId {get;set;}="";
    public int Level {get;set;}=1;
    public long Experience {get;set;}
    public string Rank {get;set;}="F";
    public int Might {get;set;}
    public int Wisdom {get;set;}
    public int Will {get;set;}
    public string CharacterStatus {get;set;}="Не создан в игре";
    public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed record CreateEtherAccount(string Nickname);

