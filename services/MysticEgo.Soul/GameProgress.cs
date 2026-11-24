namespace MysticEgo.Soul;

public sealed record GameMeter(string Id, string Title, int? Current, int? Maximum, string Formula, bool Locked = false);
public sealed record DerivedTrait(string Id, string Title, double? Value, string Unit, string Formula, bool Locked);
public sealed record GameAttribute(string Id, string Label, string Group, int? Value, bool Locked, int? Base,
    int Allocated, string? Grade, string Hint);
public sealed record ProgressSheet(int Level, string Rank, int Experience, int InLevel, int ToNext, double Percent,
    GameMeter[] Resources, DerivedTrait[] Traits, int Concealed, int AvailablePoints, int SpentPoints, int EarnedPoints);

public static class GameProgress
{
    public const int Initial = 10;
    public const int Cap = 100;
    private const string Embodiment = "Откроется при игровом воплощении.";
    private const string BodyData = "Значение задаёт игровой мир воплощённому персонажу.";
    private static readonly string[] Ranks = ["F", "E", "D", "C", "B", "A", "S", "SS", "SSS"];
    private static readonly (string Id, string Label, string Group, string Hint)[] Shard =
    [
        ("logic", "Логика", "mind", "Разбор причин, закономерностей и противоречий."),
        ("knowledge", "Знания", "mind", "Применение сведений, полученных в мире."),
        ("curiosity", "Наблюдательность", "mind", "Поиск деталей, связей и новых маршрутов."),
        ("emotion_recognition", "Чтение эмоций", "social", "Распознавание сигналов в диалогах персонажей."),
        ("empathy", "Эмпатия", "social", "Понимание мотивов и перспектив в игровых сценах."),
        ("self_regulation", "Саморегуляция", "social", "Сохранение внимания и спокойствия в сложных сценах.")
    ];
    private static readonly (string Id, string Label)[] Body =
    [
        ("strength", "Сила"), ("agility", "Ловкость"), ("endurance", "Выносливость"),
        ("vitality", "Живучесть"), ("speed", "Скорость")
    ];

    public static Dictionary<string, int> InitialAttributes() => Shard.ToDictionary(stat => stat.Id, _ => Initial);
    public static bool CanAllocate(string id) => Shard.Any(stat => stat.Id == id);
    public static int ExperienceFor(int difficulty) => 25 + 15 * Math.Clamp(difficulty, 1, 5);
    public static int Threshold(int level) => 20 * (level - 1) * level;
    public static int Trained(SoulRecord soul, string id) => Math.Clamp(soul.Stats.GetValueOrDefault(id, Initial), Initial, Cap);
    public static int Value(SoulRecord soul, string id) => Math.Min(Cap, Trained(soul, id) + (soul.Allocations?.GetValueOrDefault(id) ?? 0));
    public static string Grade(int value) => value switch
    {
        < 15 => "Базовый", < 30 => "Практик", < 50 => "Опытный", < 75 => "Мастер", _ => "Выдающийся"
    };

    public static GameAttribute[] Attributes(SoulRecord soul)
    {
        var mind = Shard.Select(stat => new GameAttribute(stat.Id, stat.Label, stat.Group, Value(soul, stat.Id), false,
            Trained(soul, stat.Id), soul.Allocations?.GetValueOrDefault(stat.Id) ?? 0, Grade(Value(soul, stat.Id)), stat.Hint));
        var physical = Body.Select(stat =>
        {
            var value = BodyValue(soul, stat.Id);
            return new GameAttribute(stat.Id, stat.Label, "body", value, value is null, value, 0,
                value.HasValue ? Grade(value.Value) : null, soul.State == "EgoShard" ? Embodiment : BodyData);
        });
        return [..mind, ..physical];
    }

    private static int? BodyValue(SoulRecord soul, string id) => soul.State == "DigitalSoul" && soul.Stats.TryGetValue(id, out var value)
        ? Math.Clamp(value, 1, Cap) : null;

    public static ProgressSheet Calculate(SoulRecord soul)
    {
        var xp = soul.Attempts.Values.Where(attempt => attempt.Passed && SoulEngine.Quests.ContainsKey(attempt.QuestId))
            .Sum(attempt => ExperienceFor(SoulEngine.Quests[attempt.QuestId].Difficulty));
        var level = Enumerable.Range(1, 90).Last(value => Threshold(value) <= xp);
        var rank = Ranks[(level - 1) / 10];
        var local = xp - Threshold(level);
        var next = level == 90 ? 0 : Threshold(level + 1) - Threshold(level);
        var percent = next == 0 ? 100 : Math.Round(local * 100.0 / next, 1);
        var earned = (level - 1) * 3;
        var spent = soul.Allocations?.Values.Sum() ?? 0;
        var focus = Value(soul, "self_regulation");
        var logic = Value(soul, "logic");
        var knowledge = Value(soul, "knowledge");
        var observation = Value(soul, "curiosity");
        var health = 60 + level * 8 + BodyValue(soul, "vitality") * 4 + BodyValue(soul, "endurance") * 2;
        var stamina = 35 + level * 4 + BodyValue(soul, "endurance") * 4 + BodyValue(soul, "agility") * 2;
        var mana = 25 + level * 4 + logic * 2 + knowledge * 2;
        var affinity = xp >= 80;
        var will = xp >= 160;
        var bodyHint = soul.State == "EgoShard" ? Embodiment : BodyData;
        return new(level, rank, xp, local, next, percent,
        [
            Meter("hp", "ХП", health, "60 + 8 × уровень + 4 × живучесть + 2 × выносливость", bodyHint),
            Meter("mana", "Мана", mana, "25 + 4 × уровень + 2 × логика + 2 × знания", ""),
            Meter("stamina", "Стамина", stamina, "35 + 4 × уровень + 4 × выносливость + 2 × ловкость", bodyHint)
        ],
        [
            new("clarity", "Ясность", Math.Round((2 * logic + knowledge + focus) / 4.0, 1), "", "(2 × логика + знания + саморегуляция) ÷ 4", false),
            new("attention", "Внимание", Math.Round((2 * observation + logic + focus) / 4.0, 1), "", "(2 × наблюдательность + логика + саморегуляция) ÷ 4", false),
            new("harmony", "Резонанс", affinity ? Math.Round((Value(soul, "empathy") + Value(soul, "emotion_recognition") + focus) / 3.0, 1) : null, "",
                affinity ? "(эмпатия + чтение эмоций + саморегуляция) ÷ 3" : "Откроется после 80 XP", !affinity),
            .. (will ? new[] { new DerivedTrait("volition", "Воля", Math.Round((2 * focus + logic + level * 3) / 4.0, 1), "",
                "(2 × саморегуляция + логика + 3 × уровень) ÷ 4", false) } : [])
        ], (affinity ? 0 : 1) + (will ? 0 : 1), Math.Max(0, earned - spent), spent, earned);
    }

    private static GameMeter Meter(string id, string title, int? value, string formula, string locked) =>
        new(id, title, value, value, value is null ? locked : formula, value is null);
}
