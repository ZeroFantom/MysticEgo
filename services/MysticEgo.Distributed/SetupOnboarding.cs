using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net.Http.Json;
using Models.Data;
using Models.Commands;

namespace MysticEgo.Distributed;

/// <summary>Декларативная сущность первоначальной настройки. Пароли здесь не хранятся.</summary>
[Entity("mysticego.setup.organization",version:1)]
public sealed class SetupOrganization
{
    [EntityKey] public Guid Id {get;set;}
    [EntityField(Required=true)] public string Name {get;set;}="";
    public string FounderLogin {get;set;}="";
    public string Kind {get;set;}="";
    public string DepartmentsJson {get;set;}="[]";
    public string SitesJson {get;set;}="[]";
    public string StorageDatabase {get;set;}="";
    public string StorageCloud {get;set;}="";
    public string StorageLocal {get;set;}="";
    public DateTimeOffset CreatedAt {get;set;}=DateTimeOffset.UtcNow;
}
[Entity("mysticego.setup.invitation",version:1)]
public sealed class SetupInvitation
{
    [EntityKey] public Guid Id {get;set;}
    [EntityField(Required=true)] public string Hash {get;set;}="";
    public string ChildKind {get;set;}="";
    public string IssuedBy {get;set;}="";
    public DateTimeOffset ExpiresAt {get;set;}
    public DateTimeOffset? UsedAt {get;set;}
    public string? ConnectedNodeId {get;set;}
}
public sealed record SetupDepartment(string Name,int Members);
public sealed record SetupSite(string Name,string Address,int Computers);
public sealed record SetupOrganizationRequest(string Name,string FounderLogin,
    SetupDepartment[] Departments,SetupSite[] Sites,string StorageDatabase,
    string StorageCloud,string StorageLocal);
public sealed record SetupInviteRequest(string ChildKind,int ValidMinutes=30);
public sealed record SetupEnrollRequest(string Code,string ChildKind,string NodeId,string DisplayName);
public sealed record SetupJoinParentRequest(string Code);
[Entity("mysticego.setup.team.invitation",version:1)]
public sealed class SetupTeamInvitation
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string CodeHash {get;set;}="";
    public string Department {get;set;}="";
    public string Label {get;set;}="";
    public DateTimeOffset ExpiresAt {get;set;}
    public string? RegisteredLogin {get;set;}
}
public sealed record CreateTeamInviteRequest(string Department,string Label,int ValidHours=24);
public sealed record JoinTeamRequest(string InviteCode);


/// <summary>
/// Регистрация и одноразовые приглашения узлов. Приглашение НЕ является JWT агента.
/// Для production с несколькими репликами нужен транзакционный общий storage,
/// поскольку встроенный DynamicEntityStore использует локальный SQLite.
/// </summary>
