using System.Security.Claims;
using System.Text.Json;
using Models.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

/// <summary>Публичная карточка пользователя. Хранение через динамическую схему ядра.</summary>
[Entity("mysticego.social.profile",version:4)]
public sealed class SocialProfile
{
    [EntityKey] public Guid Id { get; set; }
    [EntityField(Required=true)] public string UserId {get;set;}="";
    public string DisplayName {get;set;}="";
    public Guid? AccountId {get;set;}
    public string GameNickname {get;set;}="";
    public string Login {get;set;}="";
    public string Bio {get;set;}="";
    public string AvatarUrl {get;set;}="";
    public string BackgroundUrl {get;set;}="";
    public string CoverUrl {get;set;}="";
    public string Accent {get;set;}="#a16bff";
    public string Level {get;set;}="guest";
    public bool Discoverable {get;set;}=false;
    public DateTimeOffset UpdatedAt {get;set;}=DateTimeOffset.UtcNow;
}
[Entity("mysticego.social.friendship",version:1)]
public sealed class FriendRequest
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string Sender {get;set;}="";
    [EntityField(Required=true)]public string Recipient {get;set;}="";
    public string Status {get;set;}="pending";
    public DateTimeOffset At {get;set;}=DateTimeOffset.UtcNow;
}
[Entity("mysticego.social.message",version:2)]
public sealed class SocialMessage
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string Sender {get;set;}="";
    [EntityField(Required=true)]public string Recipient {get;set;}="";
    [EntityField(Required=true)]public string Text {get;set;}="";
    public Guid? StickerId { get; set; }
    public DateTimeOffset At {get;set;}=DateTimeOffset.UtcNow;
}
[Entity("mysticego.social.notification",version:1)]
public sealed class SocialNotification
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string Recipient {get;set;}="";
    public string Kind {get;set;}="";
    public string Text {get;set;}="";
    public bool Read {get;set;}
    public DateTimeOffset At {get;set;}=DateTimeOffset.UtcNow;
}

/// <summary>Принадлежность сотрудника назначает только оператор Global.
/// Пользователь не может выдать себе должность или сеть настройкой профиля.</summary>
[Entity("mysticego.social.affiliation",version:1)]
public sealed class EmployeeAffiliation
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string UserId {get;set;}="";
    public string Network {get;set;}="";
    public string Club {get;set;}="";
    public string Position {get;set;}="";
    public DateTimeOffset UpdatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed record EmployeeAffiliationEdit(string UserId,string Network,string Club,string Position);

[Entity("mysticego.social.roleappearance",version:1)]
public sealed class RoleAppearance
{
    [EntityKey]public Guid Id {get;set;}
    [EntityField(Required=true)]public string RoleCode {get;set;}="";
    public string Color {get;set;}="#a16bff";
    public string Label {get;set;}="";
}
public sealed record RoleAppearanceEdit(string RoleCode,string Label,string Color);

public sealed record ProfileEdit(string DisplayName,string Bio,string? AvatarUrl,string? BackgroundUrl,string? CoverUrl,string? Accent,bool Discoverable=false);
public sealed record FriendEdit(string Recipient);
public sealed record MessageEdit(string Recipient,string Text,Guid? StickerId = null);

