using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Models.Data;
using Models.Commands;

namespace MysticEgo.Distributed;

/// <summary>Добровольная самооценка, НЕ психодиагностика и НЕ игровые статы сервера.</summary>
[Entity("mysticego.audit.selfassessment",version:1)]
public sealed class HomeAssessment
{
    [EntityKey] public Guid Id {get;set;}
    [EntityField(Required=true)] public string UserId {get;set;}="";
    public string Nickname {get;set;}="";
    public string AnswersJson {get;set;}="{}";
    public string AchievementsJson {get;set;}="[]";
    public int Level {get;set;}=1;
    public string Rank {get;set;}="F";
    public DateTimeOffset UpdatedAt {get;set;}=DateTimeOffset.UtcNow;
}
public sealed record HomeAssessmentEdit(string Nickname,Dictionary<string,int> Answers,string[] Achievements);
