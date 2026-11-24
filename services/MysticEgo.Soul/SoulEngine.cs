using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Soul;
// Ported minimal shard/Character V2/memory contract from v1 Python to AuditOrchestra .NET.
// Not a real-world IQ or psychological assessment. No forged identity proof references.
public sealed record MemoryEntry(string Id,string Scope,string Content,DateTimeOffset Added);
public sealed record QuestAttempt(string QuestId,bool Passed,string Stat,int Delta,string PolicyVersion,DateTimeOffset Created,
    int? Covered = null, int? Required = null)
{
    public string Grade => Passed ? "Зачтено" : "Нужна доработка";
}
public sealed record AssistantCard(string Name,string Tone,string Detail,int Initiative,string Interests,bool MemoryPersonal,bool MemoryGame,string Version="character-v2-alpha3");
public sealed record SoulRecord(string UserId,string SoulId,DateTimeOffset Created,string State,
    Dictionary<string,int> Stats,Dictionary<string,int> SelfReport,AssistantCard? Card,
    List<MemoryEntry> Memories,Dictionary<string,QuestAttempt> Attempts,List<string> Knowledge,
    DateTimeOffset? SurveyCompletedAt = null, string? SurveyVersion = null, Dictionary<string, double>? PersonalityScores = null,
    Dictionary<string, int>? PersonalityAnswers = null, Dictionary<string, int>? Allocations = null)
{
    public bool CanManifest => State=="EgoShard" && DateTimeOffset.UtcNow-Created>=TimeSpan.FromDays(7);
    public double AgeHours => Math.Round((DateTimeOffset.UtcNow-Created).TotalHours,1);
}
public sealed record CardAnswers(string Name,string Tone,string Detail,int Initiative,string Interests,bool MemoryPersonal,bool MemoryGame);
public sealed record QuestAnswers(string QuestId,string Answer);
public sealed record MemoryInput(string Scope,string Content);
public sealed record AllocateAttributes(Dictionary<string, int> Points, int ExpectedSpent);
public interface ISoulService
{
    Task<SoulRecord?> Get(string user, CancellationToken ct);
    Task<SoulRecord> Register(string user, CancellationToken ct);
    Task<SoulRecord> ApplyCard(string user, CardAnswers answers, CancellationToken ct);
    Task<object> SurveyAnswers(string user, Dictionary<string,int> answers, CancellationToken ct);
    Task<QuestAttempt> Quest(string user, QuestAnswers input, CancellationToken ct);
    Task<ProgressSheet> Allocate(string user, AllocateAttributes input, CancellationToken ct);
    Task<MemoryEntry> Remember(string user, MemoryInput input, CancellationToken ct);
    Task<object?> Context(string user, string mode, CancellationToken ct);
}

public sealed class SoulEngine(IConfiguration cfg) : ISoulService
{
    readonly string _dir=Path.GetFullPath(cfg["Soul:StoragePath"]??Path.Combine(AppContext.BaseDirectory,"souls"));
    readonly SemaphoreSlim _gate=new(1,1);
    readonly JsonSerializerOptions _json=new(JsonSerializerDefaults.Web){WriteIndented=true};
    public const string QuestionnaireVersion="personality-petals-2";
    public static readonly Dictionary<string, string> Survey = new()
    {
        ["sociability"] = "В новом месте я охотно знакомлюсь с другими персонажами.",
        ["empathy"] = "Перед решением я стараюсь понять, что чувствуют другие участники.",
        ["curiosity"] = "Я часто исследую незнакомые места, даже если это не нужно для текущей задачи.",
        ["caution"] = "Перед рискованным решением я проверяю, что может пойти не так.",
        ["independence"] = "В трудной ситуации я сначала ищу путь, который могу выбрать самостоятельно.",
        ["discipline"] = "Если я взялся за задачу, я регулярно возвращаюсь к ней до завершения.",
        ["ambition"] = "Я охотно выбираю цели, которые требуют заметного роста моих возможностей.",
        ["emotionality"] = "События игрового мира обычно вызывают у меня сильный эмоциональный отклик."
    };
    public static readonly Dictionary<string,(string Name,string Npc,string Prompt,string[] Required,string Stat,int Difficulty,string Fact)> Quests=new(){
        ["water"]=("Мольба целительницы","Элиан","Почему после дождей болеют жители возле колодца?",["вод","провер"],"knowledge",1,"Осколок помог Элиан исследовать воду."),
        ["conflict"]=("Спор у ворот","Вахтенный Ир","Как снизить напряжение между путниками?",["выслуш","спокой"],"empathy",1,"Осколок предложил выслушать обе стороны."),
        ["sigils"]=("Знаки храма","Хранитель Тир","Как проверить значение неизвестных символов?",["сравн","символ"],"logic",2,"Осколок помог сравнить символы рукописей."),
        ["route"]=("Карта путника","Гонец Лис","Как проложить безопасный путь между двумя неизвестными тропами?",["карт","развед"],"curiosity",2,"Осколок составил план маршрута."),
        ["nightwatch"]=("Ночная стража","Смотритель Тар","Как поддержать внимание отряда к концу долгой смены?",["отдых","смен"],"self_regulation",2,"Осколок согласовал порядок отдыха."),
        ["reunion"]=("Письмо домой","Сказительница Эйра","Как понять настроение героя, если он говорит, что всё хорошо?",["тон","вопрос"],"emotion_recognition",2,"Осколок внимательно поговорил с путником.")
    };
    string FileFor(string user)
    {
        Directory.CreateDirectory(_dir);
        var hash=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(user))).ToLowerInvariant();
        return Path.Combine(_dir,hash+".json");
    }
    public async Task<SoulRecord?> Get(string user,CancellationToken ct)
    {
        var file=FileFor(user);if(!File.Exists(file))return null;
        try{return JsonSerializer.Deserialize<SoulRecord>(await File.ReadAllTextAsync(file,ct),_json);}catch(JsonException){return null;}
    }
    async Task Save(SoulRecord record,CancellationToken ct)
    {
        var file=FileFor(record.UserId);var tmp=file+".tmp";
        await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(record,_json),ct);File.Move(tmp,file,true);
    }
    public async Task<SoulRecord> Register(string user,CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try{
            var previous=await Get(user,ct);if(previous is not null)return previous;
            var soul=new SoulRecord(user,Guid.NewGuid().ToString(),DateTimeOffset.UtcNow,"EgoShard",
                GameProgress.InitialAttributes(),new Dictionary<string,int>(),null,[],new Dictionary<string,QuestAttempt>(),[]);
            await Save(soul,ct);return soul;
        }finally{_gate.Release();}
    }
    public async Task<SoulRecord> ApplyCard(string user,CardAnswers answers,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(answers.Name)||answers.Name.Length>48||answers.Tone is not("friendly" or "formal" or "neutral")||answers.Detail is not("brief" or "balanced" or "detailed")||answers.Initiative is <1 or >5||answers.Interests?.Length>300)throw new ArgumentException("Invalid Character V2 answers");
        await _gate.WaitAsync(ct);
        try {var soul=await Get(user,ct)??throw new InvalidOperationException("Осколок ещё не создан.");
            var card=new AssistantCard(answers.Name.Trim(),answers.Tone,answers.Detail,answers.Initiative,answers.Interests??"",answers.MemoryPersonal,answers.MemoryGame);
            soul=soul with {Card=card};await Save(soul,ct);return soul;}
        finally{_gate.Release();}
    }
    public async Task<object> SurveyAnswers(string user,Dictionary<string,int> answers,CancellationToken ct)
    {
        var scores = PersonalitySurvey.Score(answers);
        await _gate.WaitAsync(ct);
        try{var soul=await Get(user,ct)??throw new InvalidOperationException("Осколок ещё не создан.");
            soul = soul with { PersonalityScores = scores, PersonalityAnswers = new(answers),
                SurveyCompletedAt = DateTimeOffset.UtcNow, SurveyVersion = QuestionnaireVersion };
            await Save(soul, ct);
            return new { version = QuestionnaireVersion, scores };
        }finally{_gate.Release();}
    }
    public async Task<QuestAttempt> Quest(string user,QuestAnswers input,CancellationToken ct)
    {
        if(string.IsNullOrWhiteSpace(input.QuestId)||!Quests.TryGetValue(input.QuestId,out var quest)||string.IsNullOrWhiteSpace(input.Answer)||input.Answer.Length>3000)throw new ArgumentException("Invalid demonstration quest");
        await _gate.WaitAsync(ct);
        try{var soul=await Get(user,ct)??throw new InvalidOperationException("Осколок ещё не создан.");
            if(soul.State!="EgoShard")throw new InvalidOperationException("Quest is for shards");
            if(soul.Attempts.TryGetValue(input.QuestId,out var attempt)&&attempt.Passed)return attempt;
            var covered = quest.Required.Count(term => input.Answer.Contains(term, StringComparison.OrdinalIgnoreCase));
            var passed = covered == quest.Required.Length;
            var delta=passed?Math.Min(3,1+quest.Difficulty):0;
            var result=new QuestAttempt(input.QuestId,passed,quest.Stat,delta,"demonstration-keywords-v2",DateTimeOffset.UtcNow,covered,quest.Required.Length);
            soul.Attempts[input.QuestId]=result;
            if(passed){soul.Stats[quest.Stat]=Math.Min(GameProgress.Cap,GameProgress.Trained(soul,quest.Stat)+delta);soul.Knowledge.Add(quest.Fact);}
            await Save(soul,ct);return result;
        }finally{_gate.Release();}
    }
    public async Task<ProgressSheet> Allocate(string user, AllocateAttributes input, CancellationToken ct)
    {
        if (input.Points is null || input.Points.Count == 0 || input.Points.Any(pair => !GameProgress.CanAllocate(pair.Key) || pair.Value is < 1 or > GameProgress.Cap))
            throw new ArgumentException("Распределяйте целые очки между доступными характеристиками осколка.");
        await _gate.WaitAsync(ct);
        try
        {
            var soul = await Get(user, ct) ?? throw new InvalidOperationException("Осколок ещё не создан.");
            var progress = GameProgress.Calculate(soul);
            if (input.ExpectedSpent != progress.SpentPoints) throw new InvalidOperationException("Распределение уже изменилось. Обновите профиль.");
            if (input.Points.Values.Sum() > progress.AvailablePoints || input.Points.Any(pair => GameProgress.Value(soul, pair.Key) + pair.Value > GameProgress.Cap))
                throw new ArgumentException("Недостаточно свободных очков или превышен предел характеристики.");
            var allocation = new Dictionary<string, int>(soul.Allocations ?? []);
            foreach (var (key, points) in input.Points) allocation[key] = allocation.GetValueOrDefault(key) + points;
            soul = soul with { Allocations = allocation };
            await Save(soul, ct);
            return GameProgress.Calculate(soul);
        }
        finally { _gate.Release(); }
    }
    public async Task<MemoryEntry> Remember(string user,MemoryInput input,CancellationToken ct)
    {
        if(input.Scope is not ("personal" or "game" or "shared")||string.IsNullOrWhiteSpace(input.Content)||input.Content.Length>2000)throw new ArgumentException("Invalid memory scope/content");
        await _gate.WaitAsync(ct);
        try{var soul=await Get(user,ct)??throw new InvalidOperationException("Осколок ещё не создан.");
            var memory=new MemoryEntry(Guid.NewGuid().ToString("N"),input.Scope,input.Content,DateTimeOffset.UtcNow);
            soul.Memories.Add(memory);await Save(soul,ct);return memory;
        }finally{_gate.Release();}
    }
    public async Task<object?> Context(string user, string mode, CancellationToken ct)
    {
        if (mode is not ("game" or "personal")) throw new ArgumentException("Invalid assistant context.");
        var soul = await Get(user, ct);
        if (soul is null) return null;
        var allowed = mode == "game"
            ? new[] { "game", "shared" }.Concat(soul.Card?.MemoryPersonal == true ? new[] { "personal" } : Array.Empty<string>())
            : new[] { "personal", "shared" }.AsEnumerable();
        return new
        {
            mode,
            memories = soul.Memories.Where(m => allowed.Contains(m.Scope)).TakeLast(25),
            facts = mode == "game" ? soul.Knowledge : new List<string>(),
            tools = mode == "game" ? Array.Empty<string>() : new[] { "suggest_actions_only" },
            card = soul.Card
        };
    }

}
public static class SoulRegistration
{
    public static IServiceCollection AddSoul(this IServiceCollection services)
    {
        services.AddSingleton<SoulEngine>();
        services.AddSingleton<ISoulService>(sp => sp.GetRequiredService<SoulEngine>());
        return services;
    }
}
