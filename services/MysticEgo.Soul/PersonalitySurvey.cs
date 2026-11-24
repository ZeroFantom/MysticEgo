namespace MysticEgo.Soul;

public sealed record SurveyItem(string Id, string Axis, string Text, bool Reverse = false);
public static class PersonalitySurvey
{
    public const string Legacy = "personality-petals-1";
    public static readonly SurveyItem[] Items =
    [
        new("s01","sociability","В новом месте я охотно знакомлюсь с персонажами."),
        new("e01","empathy","Я стараюсь понять, почему другой персонаж сделал свой выбор."),
        new("c01","curiosity","Я сворачиваю с маршрута, когда вижу необычное место."),
        new("p01","caution","Перед риском я проверяю, что может пойти не так."),
        new("i01","independence","В сложной ситуации я сначала ищу собственное решение."),
        new("d01","discipline","Я возвращаюсь к начатому квесту, пока не закончу его."),
        new("a01","ambition","Меня привлекают цели, для которых придётся заметно вырасти."),
        new("m01","emotionality","Неожиданные события игры вызывают у меня сильный отклик."),
        new("s02","sociability","Обычно я избегаю разговоров с незнакомыми персонажами.",true),
        new("e02","empathy","Мне интересны мотивы персонажа, даже если он со мной не согласен."),
        new("c02","curiosity","Я редко исследую то, что не относится к заданию.",true),
        new("p02","caution","Я охотно действую наугад, даже когда цена ошибки высока.",true),
        new("i02","independence","Чаще жду указаний от группы, прежде чем выбрать направление.",true),
        new("d02","discipline","Я часто бросаю начатое ради новой задачи.",true),
        new("a02","ambition","Я предпочитаю знакомые цели, даже если доступен сложный вызов.",true),
        new("m02","emotionality","Сильные моменты истории быстро перестают меня трогать.",true),
        new("s03","sociability","Я рад пригласить нового игрока в свою группу."),
        new("e03","empathy","Разбирая конфликт, я вспоминаю, что важно для каждой стороны."),
        new("c03","curiosity","Мне хочется проверять, к чему приведут разные варианты."),
        new("p03","caution","Перед трудным боем я собираю сведения о противнике."),
        new("i03","independence","Мне комфортно выбирать свой путь, даже если другие выбрали иначе."),
        new("d03","discipline","Я разбиваю длинное задание на шаги и выполняю их."),
        new("a03","ambition","Мне нравится пробовать то, что раньше казалось недостижимым."),
        new("m03","emotionality","Музыка и атмосфера игрового места заметно влияют на моё настроение.")
    ];
    public static Dictionary<string, double> Score(IReadOnlyDictionary<string, int> answers)
    {
        if (answers.Count != Items.Length || Items.Any(q => !answers.TryGetValue(q.Id, out var value) || value is < 1 or > 5))
            throw new ArgumentException("Ответьте на все 24 утверждения по шкале от 1 до 5.");
        return Items.GroupBy(item => item.Axis).ToDictionary(group => group.Key,
            group => Math.Round(group.Average(item => item.Reverse ? 6 - answers[item.Id] : answers[item.Id]), 2));
    }
}
