using Models.Commands;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;
using MysticEgo.Soul;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController, Authorize(Policy = "operator"), Route("api/v1/collective")]
public sealed class CollectiveController(
    SocialService social, ISocialAccessPolicy access, ISoulService souls, IConfiguration config, NodeState state, CollectiveVoice voice, ILogger<CollectiveController> logger) : ControllerBase
{
    private Guid Actor()
    {
        if(User.Identity?.IsAuthenticated!=true||User.HasClaim("token_use","device")||
            !Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier),out var id)||id==Guid.Empty)
            throw new NodeApiException(401,"Сеанс не содержит действительного ID аккаунта. Войдите повторно.");
        return id;
    }
    private string Nickname => User.FindFirstValue("nickname") ?? User.Identity?.Name ?? "Игрок";
    private static readonly (string Id, string Label)[] Axes =
    [
        ("sociability", "Социальность"), ("empathy", "Эмпатия"), ("curiosity", "Любознательность"),
        ("caution", "Осторожность"), ("independence", "Самостоятельность"), ("discipline", "Дисциплина"),
        ("ambition", "Амбициозность"), ("emotionality", "Эмоциональность")
    ];

    [HttpGet("me")]
    [ApiCapability("collective.profile.read", "Прочитать профиль, игровой статус и анкету", "all")]
    public Task<IActionResult> Me(CancellationToken ct) => Run<object>(async () =>
    {
        var id = Actor();
        var profile = await social.EnsureAccount(id, Nickname, User.Identity!.Name!, access.Level(User), ct);
        var soul = await souls.Register(id.ToString(), ct);
        var created = DateTimeOffset.TryParse(User.FindFirstValue("account_created"), out var registered)
            ? registered : soul.Created;
        var immortal = soul.State == "EgoShard";
        var progress = GameProgress.Calculate(soul);
        return new
        {
            profile, progress, game = new { id, nickname = Nickname, progress.Rank, progress.Level, soul.State,
                created, immortal, ageHours = Math.Max(0, soul.AgeHours), expiresAt = (DateTimeOffset?)null },
            stats = GameProgress.Attributes(soul),
            personality = Survey(soul)
        };
    });

    [HttpGet("profile")]
    public Task<IActionResult> BasicProfile(CancellationToken ct)=>Run(async()=>
        await social.EnsureAccount(Actor(),Nickname,User.Identity?.Name??Nickname,access.Level(User),ct));

    [HttpPost("shard/attributes"), ApiCapability("game.attributes.allocate", "Распределить очки развития осколка", "all", RequiresConfirmation = true)]
    public async Task<IActionResult> Allocate(AllocateAttributes input, CancellationToken ct)
    {
        var user = Actor().ToString();
        await souls.Register(user, ct);
        try { return Ok(await souls.Allocate(user, input, ct)); }
        catch (ArgumentException error) { return BadRequest(new { error = error.Message }); }
        catch (InvalidOperationException error) { return Conflict(new { error = error.Message }); }
    }

    [HttpPut("me")]
    [ApiCapability("collective.profile.edit", "Сохранить оформление профиля", "all", RequiresConfirmation = true)]
    public async Task<IActionResult> Edit(ProfileEdit edit, CancellationToken ct)
    {
        await social.EnsureAccount(Actor(), Nickname, User.Identity!.Name!, access.Level(User), ct);
        return await Run(() => social.UpdateProfile(Actor().ToString(), access.Level(User), edit, ct));
    }

    [HttpPost("personality")]
    [ApiCapability("game.personality.complete", "Сохранить ответы и восемь значений диаграммы", "all", RequiresConfirmation = true)]
    public async Task<IActionResult> Answer(PersonalityAnswers input, CancellationToken ct)
    {
        if (!input.Consent || input.Version != SoulEngine.QuestionnaireVersion)
            return BadRequest(new { error = "Подтвердите сохранение ответов и используйте текущую версию теста." });
        var user = Actor().ToString();
        await souls.Register(user, ct);
        try
        {
            await souls.SurveyAnswers(user, input.Answers, ct);
            return Ok(Survey((await souls.Get(user, ct))!));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("people")]
    [ApiCapability("collective.search", "Найти игрока по никнейму или ID", "all")]
    public async Task<IActionResult> People([FromQuery] string? q, CancellationToken ct) =>
        Ok((await social.FindPeople((q ?? "").Trim(), Actor(), ct)).Select(Card));

    [HttpGet("contacts")]
    public async Task<IActionResult> Contacts(CancellationToken ct)
    {
        var id = Actor().ToString();
        var friends = (FriendRequest[])await social.Friends(id, ct);
        var latest = ((SocialMessage[])await social.Messages(id, ct))
            .GroupBy(message => message.Sender == id ? message.Recipient : message.Sender)
            .ToDictionary(group => group.Key, group => group.First());
        var contacts = new List<object>();
        foreach (var friend in friends)
        {
            var partner = await social.FindProfile(friend.Sender == id ? friend.Recipient : friend.Sender, ct);
            if (partner is not null) contacts.Add(new
            {
                friend.Id, friend.Status, incoming = friend.Recipient == id, person = Card(partner),
                last = friend.Status == "accepted" && latest.TryGetValue(partner.Id.ToString(), out var message)
                    ? new { message.Text, message.At, message.StickerId, voice = message.VoiceMime is not null, mine = message.Sender == id }
                    : null
            });
        }
        return Ok(contacts);
    }

    [HttpPost("contacts")]
    [ApiCapability("collective.contacts.request", "Отправить запрос на добавление в контакты", "all", RequiresConfirmation = true)]
    public async Task<IActionResult> RequestContact(ContactInput input, CancellationToken ct)
    {
        await social.EnsureAccount(Actor(), Nickname, User.Identity!.Name!, access.Level(User), ct);
        if (await social.FindProfile(input.Id.ToString(), ct) is null) return NotFound();
        return await Run(() => social.RequestFriend(Actor().ToString(), new(input.Id.ToString()), ct));
    }

    [HttpPost("contacts/{id:guid}/accept")]
    public Task<IActionResult> Accept(Guid id, CancellationToken ct) =>
        Run(() => social.AcceptFriend(Actor().ToString(), id, ct));

    [HttpGet("messages/{peer:guid}")]
    [ApiCapability("collective.messages.read", "Прочитать переписку с контактом", "all")]
    public Task<IActionResult> Messages(Guid peer, [FromQuery] DateTimeOffset? before, CancellationToken ct) =>
        Run(() => social.Conversation(Actor().ToString(), peer.ToString(), before, ct));

    [HttpPost("messages/{peer:guid}")]
    [ApiCapability("collective.messages.send", "Отправить сообщение контакту", "all", RequiresConfirmation = true)]
    public async Task<IActionResult> Send(Guid peer, ChatInput input, CancellationToken ct)
    {
        if (input.StickerId is { } id)
        {
            var sticker = await state.Data.GetAsync<Sticker>(id, ct);
            if (sticker is not { Saved: true } || sticker.Owner != Actor()) return NotFound();
        }
        return await Run(() => social.SendMessage(Actor().ToString(), new(peer.ToString(), input.Text ?? "", input.StickerId, input.ReplyToId), ct));
    }

    [HttpPut("messages/{id:guid}")]
    public Task<IActionResult> EditMessage(Guid id, ChatInput input, CancellationToken ct) =>
        Run(() => social.EditMessage(Actor().ToString(), id, input.Text, ct));

    [HttpDelete("messages/{id:guid}")]
    public Task<IActionResult> DeleteMessage(Guid id, CancellationToken ct) => Run(async () =>
    {
        var message = await social.DeleteMessage(Actor().ToString(), id, ct);
        voice.Delete(id);
        return message;
    });

    [HttpPost("messages/{id:guid}/forward")]
    public Task<IActionResult> Forward(Guid id, ContactInput input, CancellationToken ct) => Run(async () =>
    {
        var actor = Actor().ToString();
        var source = await social.OpenMessage(actor, id, ct);
        var copy = Guid.NewGuid();
        try
        {
            if (source.VoiceMime is not null)
            {
                await using var audio = voice.Open(id);
                await voice.Save(copy, audio, ct);
            }
            return await social.ForwardMessage(actor, input.Id.ToString(), copy, source, ct);
        }
        catch
        {
            voice.Delete(copy);
            throw;
        }
    });

    [HttpPost("voice/{peer:guid}"), RequestSizeLimit(CollectiveVoice.MaxBytes)]
    public Task<IActionResult> SendVoice(Guid peer, [FromQuery] Guid? replyTo, CancellationToken ct) => Run(async () =>
    {
        var id = Guid.NewGuid();
        try
        {
            var mime = await voice.Save(id, Request.Body, ct);
            return await social.SendVoice(Actor().ToString(), peer.ToString(), id, mime, replyTo, ct);
        }
        catch
        {
            voice.Delete(id);
            throw;
        }
    });

    [HttpGet("voice/{id:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Voice(Guid id, CancellationToken ct)
    {
        try
        {
            var message = await social.OpenMessage(Actor().ToString(), id, ct);
            if (message.VoiceMime is null) return NotFound();
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(voice.Open(id), message.VoiceMime);
        }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
    }

    [HttpPost("voice/{id:guid}/transcript")]
    public Task<IActionResult> Transcript(Guid id, CancellationToken ct) => Run(async () =>
    {
        var actor = Actor().ToString();
        var message = await social.OpenMessage(actor, id, ct);
        if (message.VoiceMime is null) throw new NodeApiException(404, "Аудио не найдено.");
        if (message.Transcript is not null) return message;
        return await social.SaveTranscript(actor, id, await voice.Transcribe(message, ct), ct);
    });

    [HttpGet("notifications")]
    public Task<IActionResult> Notifications(CancellationToken ct) =>
        Run(() => social.Notifications(Actor().ToString(), ct));

    [HttpPost("notifications/{id:guid}/read")]
    public Task<IActionResult> Read(Guid id, CancellationToken ct) =>
        Run(() => social.ReadNotification(Actor().ToString(), id, ct));

    private object Survey(SoulRecord soul)
    {
        var scores = soul.PersonalityScores ?? soul.SelfReport.ToDictionary(x => x.Key, x => (double)x.Value);
        var completed = soul.SurveyCompletedAt.HasValue && Axes.All(axis => scores.ContainsKey(axis.Id));
        return new
        {
            version = SoulEngine.QuestionnaireVersion, resultVersion = completed ? soul.SurveyVersion : null, completed,
            completedAt = completed ? soul.SurveyCompletedAt : null,
            questions = PersonalitySurvey.Items.Select(item => new { item.Id, item.Axis, item.Text }),
            axes = Axes.Select(axis => new {
                axis.Id, label = config[$"Game:Personality:{axis.Id}:Label"] ?? axis.Label,
                question = SoulEngine.Survey[axis.Id], value = completed ? (double?)scores[axis.Id] : null
            })
        };
    }

    [HttpGet("shard/quests")]
    public Task<IActionResult> Quests(CancellationToken ct) => Run<object>(async () =>
    {
        var soul = await souls.Register(Actor().ToString(), ct);
        return SoulEngine.Quests.Select(item => new { id = item.Key, title = item.Value.Name,
            item.Value.Npc, item.Value.Prompt, item.Value.Difficulty, stat = item.Value.Stat, experience = GameProgress.ExperienceFor(item.Value.Difficulty),
            attempt = soul.Attempts.GetValueOrDefault(item.Key) }).ToArray();
    });

    [HttpPost("shard/quests")]
    public async Task<IActionResult> Quest(QuestAnswers input, CancellationToken ct)
    {
        var actor = Actor().ToString();
        await souls.Register(actor, ct);
        try { return Ok(await souls.Quest(actor, input, ct)); }
        catch (ArgumentException) { return BadRequest(new { error = "Укажите задание и ответ от 1 до 3000 символов." }); }
        catch (InvalidOperationException error) { return Conflict(new { error = error.Message }); }
    }

    private static object Card(SocialProfile x) => new { x.Id, x.DisplayName, nickname = x.GameNickname, x.AvatarUrl, x.Accent, x.StatusText };
    private async Task<IActionResult> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (NodeApiException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
        catch(Exception ex) when(ex is System.Data.Common.DbException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
        {
            var trace=HttpContext.TraceIdentifier;
            logger.LogError("Collective operation failed: {Action} {ExceptionType} trace={Trace}", ControllerContext.ActionDescriptor.ActionName,ex.GetType().Name,trace);
            return StatusCode(503,new{error="Хранилище аккаунта временно недоступно. Данные не сбрасываются. Код: "+trace,trace});
        }
    }

    public sealed record PersonalityAnswers(string Version, bool Consent, Dictionary<string, int> Answers);
    public sealed record ContactInput(Guid Id);
    public sealed record ChatInput(string? Text, Guid? StickerId = null, Guid? ReplyToId = null);
}
