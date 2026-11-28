using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Commands;
using Models.Data;

namespace MysticEgo.Distributed;

[Entity("mysticego.character.v2", 1)]
public sealed class CharacterV2
{
    [EntityKey] public Guid Id { get; set; }
    public string Name { get; set; } = "Эхо";
    public string Tone { get; set; } = "friendly";
    public string Detail { get; set; } = "balanced";
    public int Initiative { get; set; } = 2;
    public string Interests { get; set; } = "Игры, устройство компьютера и MysticEgo";
    public string Mood { get; set; } = "calm";
    public string Music { get; set; } = "auto";
    public bool Voice { get; set; } = true;
    public bool MoveDesktop { get; set; } = true;
    public string Version { get; set; } = "character-v2";
}

public sealed class AssistantPersonas(NodeState state)
{
    private static Guid Key(string owner) => new(SHA256.HashData(Encoding.UTF8.GetBytes("character:" + owner)).AsSpan(0, 16));
    public async Task<CharacterV2> Read(string owner, CancellationToken ct) =>
        await state.Data.GetAsync<CharacterV2>(Key(owner), ct) ?? new() { Id = Key(owner) };
    public async Task<CharacterV2> Save(string owner, CharacterV2 character, CancellationToken ct)
    {
        Check(character);
        character.Id = Key(owner);
        character.Version = "character-v2";
        await state.Data.PutAsync(character, ct);
        return character;
    }
    public static void Check(CharacterV2 character)
    {
        if (string.IsNullOrWhiteSpace(character.Name) || character.Name.Length > 48 || character.Interests?.Length > 300 ||
            character.Tone is not ("friendly" or "formal" or "neutral") || character.Detail is not ("brief" or "balanced" or "detailed") ||
            character.Initiative is < 1 or > 5 || character.Mood is not ("calm" or "lively" or "focused") ||
            character.Music is not ("auto" or "dance" or "listen"))
            throw new AssistantApiException(400, "Проверьте поля Character V2.");
    }
}

[ApiController, Authorize(Policy = "operator"), Route("api/v1/assistant")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AssistantWorkbenchController(ILocalAssistantService assistant, ILmStudioClient model, AssistantPersonas personas,
    IAssistantAccessPolicy access, IAssistantProposalStore proposals, NodeOptions node, AssistantConversations conversations) : ControllerBase
{
    private string? DeviceActor => node.Kind == NodeKind.Orchestra && !EcosystemHierarchy.IsDeveloper(User) ? null : access.ResolveOwner(User);
    private string? Actor => DeviceActor ?? (node.Kind == NodeKind.Orchestra && Account != Guid.Empty ? User.Identity?.Name : null);
    private Guid Account => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string Generator = """
        Ты генератор персонального игрового ассистента MysticEgo Character V2.
        Верни только один JSON-объект без Markdown со следующими полями: name (до 48 символов),
        tone (friendly, formal или neutral), detail (brief, balanced или detailed), initiative (целое 1..5),
        interests (до 300 символов), mood (calm, lively или focused), music (auto, dance или listen),
        voice (boolean), moveDesktop (boolean). Не выводи личные диагнозы, содержимое файлов или права доступа.
        Пользователь сначала увидит черновик и сможет изменить его перед сохранением. /no_think
        """;

    private bool MobileAccount => node.Kind==NodeKind.Orchestra && Actor is not null && Account!=Guid.Empty && !User.HasClaim("token_use","device") && Guid.TryParse(User.FindFirst("mystic_session")?.Value,out _);

    [HttpGet("mobile/providers")]
    public IActionResult MobileProviders([FromServices] Microsoft.Extensions.Configuration.IConfiguration config) => MobileAccount ? Ok(new{cloud=MobileInference.ChatConfigured(config),speech=MobileInference.SpeechConfigured(config)}) : Forbid();

    public sealed record MobilePrompt(string Text,bool Consent);
    [HttpPost("mobile/cloud"),RequestSizeLimit(16384)]
    public Task<IActionResult> MobileCloud(MobilePrompt input,[FromServices] Microsoft.Extensions.Configuration.IConfiguration config,CancellationToken ct) =>
        !MobileAccount||!input.Consent?Task.FromResult<IActionResult>(Forbid()):Run(async()=>{Validate(input.Text,4000);return new{text=await MobileInference.Chat(Account,input.Text.Trim(),config,ct)};});

    [HttpPost("mobile/speech"),RequestSizeLimit(5*1024*1024)]
    public Task<IActionResult> MobileSpeech([FromServices] Microsoft.Extensions.Configuration.IConfiguration config,CancellationToken ct) =>
        !MobileAccount||Request.Headers["X-Mystic-Speech-Consent"].ToString()!="1"?Task.FromResult<IActionResult>(Forbid()):Run(async()=>new{text=await MobileInference.Speech(Account,Request.Body,config,ct)});

    [HttpGet("workbench")]
    public async Task<IActionResult> Status(CancellationToken ct) => Actor is { } actor ?
        Ok(new { service = await assistant.StatusAsync(ct), character = await personas.Read(actor, ct),
            model = node.Kind is NodeKind.Orchestra or NodeKind.Global ? "Qwen3 8B Q4_K_M" : node.Kind == NodeKind.Local ? "Qwen3 4B Q4_K_M" : "Qwen3 1.7B Q4_K_M" }) : Forbid();

    [HttpPost("workbench/chat")]
    public Task<IActionResult> Chat(LmChatRequest request, CancellationToken ct) =>
        Actor is { } actor && Account != Guid.Empty ? Run(() => conversations.Reply(Account, actor, assistant, request, DeviceActor is not null, ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpGet("workbench/history")]
    public Task<IActionResult> History(CancellationToken ct) => Actor is not null && Account != Guid.Empty ?
        Run(() => conversations.Read(Account, ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpDelete("workbench/history")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        if (Actor is null || Account == Guid.Empty) return Forbid();
        try { await conversations.Clear(Account, ct); return NoContent(); }
        catch (AssistantApiException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
    }

    [HttpPost("workbench/proposals/{id:guid}/confirm")]
    public Task<IActionResult> Confirm(Guid id, CancellationToken ct) =>
        DeviceActor is { } actor ? Run(() => assistant.ConfirmAsync(actor, id, ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpGet("persona"), ApiCapability("assistant.character.read", "Прочитать Character V2", "all")]
    public async Task<IActionResult> Persona(CancellationToken ct) => Actor is { } actor ? Ok(await personas.Read(actor, ct)) : Forbid();

    [HttpPut("persona")]
    public Task<IActionResult> Save(CharacterV2 input, CancellationToken ct) =>
        Actor is { } actor ? Run(() => personas.Save(actor, input, ct)) : Task.FromResult<IActionResult>(Forbid());

    [HttpPost("persona/generate"), ApiCapability("assistant.character.generate", "Создать черновик Character V2 локальной моделью", "all", RequiresConfirmation = true)]
    public Task<IActionResult> Generate(TextInput input, CancellationToken ct) =>
        Actor is null ? Task.FromResult<IActionResult>(Forbid()) : Run(async () =>
        {
            Validate(input.Text, 1200);
            var raw = await model.TextAsync(input.Text, Generator, ct);
            try
            {
                var from = raw.IndexOf('{'); var to = raw.LastIndexOf('}');
                if (from < 0 || to < from) throw new JsonException();
                var draft = JsonSerializer.Deserialize<CharacterV2>(raw[from..(to + 1)], Json) ?? throw new JsonException();
                AssistantPersonas.Check(draft);
                draft.Id = Guid.Empty;
                draft.Version = "character-v2";
                return draft;
            }
            catch (JsonException) { throw new AssistantApiException(422, "Модель вернула неподходящую карточку. Уточните описание или настройте поля вручную."); }
        });

    [HttpPost("polish"), ApiCapability("assistant.draft.polish", "Предложить правку черновика сообщения", "all")]
    public Task<IActionResult> Polish(TextInput input, CancellationToken ct) => Run(async () =>
    {
        Validate(input.Text, 2000);
        var output = await model.TextAsync(input.Text, "Отредактируй опечатки и ясность короткого сообщения на его языке. Сохрани смысл, тон, имена и эмодзи. Верни только исправленный черновик. Никаких действий. /no_think", ct);
        return new { text = output.Length > 2000 ? output[..2000] : output };
    });

    [HttpPost("workbench/propose")]
    public IActionResult Propose(ProposeInput input)
    {
        if (DeviceActor is not { } actor) return Forbid();
        if (input.Type is not ("launch_application" or "web_search" or "youtube_search" or "computer_status") ||
            input.Argument.Length is < 1 or > 200 ||
            input.Type == "launch_application" && (!System.Text.RegularExpressions.Regex.IsMatch(input.Argument, "^[A-Za-z0-9_.-]{1,40}$") ||
                node.Kind is not (NodeKind.Agent or NodeKind.AuditOrchestra))) return BadRequest();
        return Ok(proposals.Create(actor, [(input.Type, input.Argument)]));
    }

    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (AssistantApiException error) { return StatusCode(error.StatusCode, new { error = error.Message }); }
    }
    private static void Validate(string text, int max)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > max) throw new AssistantApiException(400, $"Введите текст до {max} символов.");
    }
    public sealed record TextInput(string Text);
    public sealed record ProposeInput(string Type, string Argument);
}
