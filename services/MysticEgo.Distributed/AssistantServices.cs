using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models.Commands;
using MysticEgo.Deployment;

namespace MysticEgo.Distributed;

public sealed class AssistantApiException(int statusCode, string message) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}

public interface IAssistantAccessPolicy
{
    string? ResolveOwner(ClaimsPrincipal principal);
}

public sealed class AssistantAccessPolicy(NodeOptions node, INodeAccessPolicy? access = null) : IAssistantAccessPolicy
{
    public string? ResolveOwner(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirst("token_use")?.Value == "device") return null;
        var user = principal.Identity.Name ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(user)) return null;
        var configuredOwner = Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");
        if (node.Kind is NodeKind.Orchestra or NodeKind.Global or NodeKind.Local)
            return access?.Operator(principal) == true ? user : null;
        if (!string.IsNullOrWhiteSpace(configuredOwner) && string.Equals(user, configuredOwner, StringComparison.Ordinal))
            return user;
        return node.Kind == NodeKind.Agent && node.Managed && access?.Operator(principal) == true ||
            node.DeploymentMode == "single-machine" && EcosystemHierarchy.IsDeveloper(principal) ? user : null;
    }
}

public interface IAssistantProposalStore
{
    IReadOnlyList<LmProposalResponse> Create(string owner, IEnumerable<(string Type, string Argument)> actions);
    bool TryConsume(Guid id, string owner, out LmProposal? proposal);
}

/// <summary>Proposal confirmation is one-shot, owner-scoped and time-limited.</summary>
public sealed class AssistantProposalStore : IAssistantProposalStore
{
    private readonly ConcurrentDictionary<Guid, LmProposal> _proposals = new();
    private readonly object _gate = new();
    public IReadOnlyList<LmProposalResponse> Create(string owner, IEnumerable<(string Type, string Argument)> actions)
    {
        var now = DateTimeOffset.UtcNow;
        var result = new List<LmProposalResponse>();
        lock (_gate)
        {
            foreach (var kv in _proposals.ToArray())
                if (now - kv.Value.CreatedAt > TimeSpan.FromMinutes(5)) _proposals.TryRemove(kv.Key, out _);
            if (_proposals.Count > 2048) throw new AssistantApiException(429, "Очередь подтверждений переполнена.");
            foreach (var (type, argument) in actions.Take(4))
            {
                var proposal = new LmProposal(Guid.NewGuid(), owner, type, argument, now);
                _proposals[proposal.Id] = proposal;
                result.Add(new LmProposalResponse(proposal.Id, type, argument));
            }
        }
        return result;
    }

    public bool TryConsume(Guid id, string owner, out LmProposal? proposal)
    {
        lock (_gate)
        {
            proposal = null;
            if (!_proposals.TryGetValue(id, out var found) ||
                !string.Equals(found.Owner, owner, StringComparison.Ordinal) ||
                DateTimeOffset.UtcNow - found.CreatedAt > TimeSpan.FromMinutes(5)) return false;
            return _proposals.TryRemove(id, out proposal);
        }
    }
}

public interface ILmStudioClient
{
    Task<LmStatusResponse> StatusAsync(CancellationToken ct);
    Task<LocalModelReply> CompleteAsync(IReadOnlyList<LocalModelMessage> messages, string system, string? selected, bool proposals, bool deviceTools, CancellationToken ct);
    Task<string> TextAsync(string text, string system, CancellationToken ct);
}

public sealed class LmStudioHttpClient(HttpClient http, NodeOptions node, IConfiguration config, ILogger<LmStudioHttpClient> logger) : ILmStudioClient
{
    private Task<LocalModels.Active?> Model(CancellationToken ct) =>
        LocalModels.Ready(http, node.Kind.ToString(), config["LmStudio:VerifiedModelFile"], ct);

    public async Task<LmStatusResponse> StatusAsync(CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            var model = await Model(timeout.Token);
            return new(model is not null, "127.0.0.1:1234", model?.Id ?? "LM Studio готовит модель. Ход запуска доступен в журнале MysticEgo.");
        }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or InvalidOperationException or KeyNotFoundException)
        { return new(false, "127.0.0.1:1234", "LM Studio пока недоступна. Проверьте журнал MysticEgo."); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new(false, "127.0.0.1:1234", "LM Studio не ответила вовремя."); }
    }

    public async Task<string> TextAsync(string text, string system, CancellationToken ct) =>
        (await CompleteAsync([new("user", text)], system, null, false, false, ct)).Text;

    public async Task<LocalModelReply> CompleteAsync(IReadOnlyList<LocalModelMessage> messages, string system, string? selected,
        bool proposals, bool deviceTools, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(145));
        try
        {
            var ready = await Model(timeout.Token);
            if (ready is null) throw new AssistantApiException(503, "Модель ещё готовится. Проверьте журнал запуска MysticEgo.");
            if (!string.IsNullOrWhiteSpace(selected) && selected != ready.Id)
                throw new AssistantApiException(400, "Указанная модель не загружена для этого узла.");
            object? tools = null;
            if (proposals)
            {
                var options = new List<object> {
                    Tool("web_search", "Предложить открыть поисковый запрос в браузере.", "query"),
                    Tool("youtube_search", "Предложить открыть поиск видео на YouTube.", "query")
                };
                if (deviceTools)
                {
                    options.Add(Tool("computer_status", "Предложить проверку состояния компьютера.", "topic"));
                    if (node.Kind is NodeKind.Agent or NodeKind.AuditOrchestra)
                        options.Add(Tool("launch_application", "Предложить запуск установленного приложения по псевдониму.", "alias"));
                }
                tools = options;
            }
            return await LocalModelChat.Respond(http, ready.Id, system, messages, 720, .4, tools, timeout.Token, ready.Key);
        }
        catch (HttpRequestException) { throw new AssistantApiException(503, "LM Studio недоступна."); }
        catch (IOException error)
        {
            logger.LogWarning("LM Studio did not complete the reply: {Message}", error.Message);
            throw new AssistantApiException(502, "LM Studio не вернула итоговый ответ. Проверьте журнал узла.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AssistantApiException(504, "Время ответа LM Studio истекло."); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException)
        { throw new AssistantApiException(502, "Некорректный ответ от LM Studio."); }
    }

    private static object Tool(string name, string description, string key) => new
    {
        type = "function", function = new { name, description, parameters = new
        {
            type = "object", properties = new Dictionary<string, object> { [key] = new { type = "string" } }, required = new[] { key }
        }}
    };
}

public interface ILocalAssistantService
{
    Task<LmStatusResponse> StatusAsync(CancellationToken ct);
    Task<LmChatResponse> ChatAsync(string owner, LmChatRequest request, CancellationToken ct,
        IReadOnlyList<LocalModelMessage>? context = null, bool deviceTools = true);
    Task<object> ConfirmAsync(string owner, Guid id, CancellationToken ct);
}

public sealed class LocalAssistantService(ILmStudioClient client, IAssistantProposalStore store, IApprovedActionExecutor actions, AssistantPersonas personas, NodeOptions node) : ILocalAssistantService
{
    private static readonly Regex AppAlias = new("^[A-Za-z0-9_.-]{1,40}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    public Task<LmStatusResponse> StatusAsync(CancellationToken ct) => client.StatusAsync(ct);

    public async Task<LmChatResponse> ChatAsync(string owner, LmChatRequest request, CancellationToken ct,
        IReadOnlyList<LocalModelMessage>? context = null, bool deviceTools = true)
    {
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > 4000)
            throw new AssistantApiException(400, "Текст должен содержать от 1 до 4000 символов.");
        var character = await personas.Read(owner, ct);
        var system = "Ты — локальный помощник MysticEgo. Отвечай на языке собеседника. Подтверждения действий выдаёт человек. " +
            "Стиль персонажа: " + character.Name + ", " + character.Tone + ", " + character.Detail + ". Интересы: " + character.Interests;
        if (!string.IsNullOrWhiteSpace(node.ClubDescription)) system += ". Справка клуба: " + node.ClubDescription;
        var result = await client.CompleteAsync([..(context ?? []), new("user", request.Text.Trim())], system + " /no_think",
            request.Model, deviceTools, deviceTools, ct);
        var proposals = new List<(string Type, string Argument)>();
        if (deviceTools && result.Tools.ValueKind == JsonValueKind.Array)
        {
            foreach (var call in result.Tools.EnumerateArray().Take(4))
            {
                if (call.ValueKind != JsonValueKind.Object || !call.TryGetProperty("function", out var function)) continue;
                var name = LocalModels.Text(function, "name");
                var args = LocalModels.Text(function, "arguments");
                if (args is null || name is not ("launch_application" or "web_search" or "youtube_search" or "computer_status")) continue;
                if (name == "launch_application" && node.Kind is not (NodeKind.Agent or NodeKind.AuditOrchestra)) continue;
                try
                {
                    using var values = JsonDocument.Parse(args);
                    var key = name switch { "launch_application" => "alias", "computer_status" => "topic", _ => "query" };
                    var argument = LocalModels.Text(values.RootElement, key);
                    if (argument is null || argument.Length > 200 || name == "launch_application" && !AppAlias.IsMatch(argument)) continue;
                    proposals.Add((name, argument));
                }
                catch (JsonException) { }
            }
        }
        if (result.Text.Length == 0 && proposals.Count == 0) throw new AssistantApiException(502, "Модель не вернула итоговый ответ.");
        return new(result.Text.Length > 8000 ? result.Text[..8000] : result.Text, store.Create(owner, proposals));
    }

    public async Task<object> ConfirmAsync(string owner, Guid id, CancellationToken ct)
    {
        if (!store.TryConsume(id, owner, out var proposal) || proposal is null)
            throw new AssistantApiException(404, "Предложение не найдено или истекло.");
        return await actions.ExecuteAsync(owner, proposal, ct);
    }
}

public interface IApprovedActionExecutor
{
    Task<object> ExecuteAsync(string owner, LmProposal proposal, CancellationToken ct);
}

public sealed class ApprovedActionExecutor(NodeOptions node) : IApprovedActionExecutor
{
    public async Task<object> ExecuteAsync(string owner, LmProposal proposal, CancellationToken ct)
    {
        if (proposal.Type == "launch_application")
        {
            if (node.Kind is not (NodeKind.Agent or NodeKind.AuditOrchestra) || !Environment.UserInteractive)
                throw new AssistantApiException(403, "Запуск приложения доступен в интерактивной сессии Agent или Audit.");
            var result = await new LaunchAllowlistedAppCommand().ExecuteAsync(new CommandContext
            {
                ExecutionId = Guid.NewGuid(), CommandId = CommandId.FromName("app.launch"),
                RawArgs = proposal.Argument, CallerLogin = owner
            }, ct);
            if (!result.Success) throw new AssistantApiException(400, "Не удалось запустить разрешённое приложение.");
            return result;
        }
        if (proposal.Type is "web_search" or "youtube_search")
        {
            var query = Uri.EscapeDataString(proposal.Argument);
            return new { url = proposal.Type == "web_search" ? "https://www.google.com/search?q=" + query :
                "https://www.youtube.com/results?search_query=" + query };
        }
        if (proposal.Type == "computer_status")
            return await new SystemInfoCommand().ExecuteAsync(new CommandContext
            {
                ExecutionId = Guid.NewGuid(), CommandId = CommandId.FromName("system.info"), CallerLogin = owner
            }, ct);
        throw new AssistantApiException(400, "Неизвестный тип подтверждения.");
    }
}

public static class AssistantDependencyInjection
{
    public static IServiceCollection AddLocalAssistant(this IServiceCollection services)
    {
        services.AddSingleton<IAssistantAccessPolicy, AssistantAccessPolicy>();
        services.AddSingleton<AssistantPersonas>();
        services.AddSingleton<AssistantConversations>();
        services.AddSingleton<IAssistantProposalStore, AssistantProposalStore>();
        services.AddSingleton<IApprovedActionExecutor, ApprovedActionExecutor>();
        services.AddScoped<ILocalAssistantService, LocalAssistantService>();
        services.AddHttpClient<ILmStudioClient, LmStudioHttpClient>()
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        return services;
    }
}
