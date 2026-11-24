// ════════════════════════════════════════════════════════════════════
//  Models.Contracts — единый файл
//  Всё, что летает по сети: capabilities, execution, SignalR.
// ════════════════════════════════════════════════════════════════════
//
//  csproj:
//  <Project Sdk="Microsoft.NET.Sdk">
//    <PropertyGroup>
//      <TargetFramework>net10.0</TargetFramework>
//      <Nullable>enable</Nullable>
//      <ImplicitUsings>enable</ImplicitUsings>
//      <RootNamespace>Models.Contracts</RootNamespace>
//      <LangVersion>latest</LangVersion>
//    </PropertyGroup>
//
//    <ItemGroup>
//      <ProjectReference Include="..\Models.Identifiers\Models.Identifiers.csproj" />
//      <ProjectReference Include="..\Models.Commands\Models.Commands.csproj" />
//    </ItemGroup>
//
//    <ItemGroup>
//      <FrameworkReference Include="Microsoft.AspNetCore.App" />
//    </ItemGroup>
//  </Project>
//
//  Философия:
//    • Никаких magic strings — только CommandId, PermissionId, ToolId.
//    • Всё иммутабельно (records).
//    • Плоская структура — рефлексия и JSON без сюрпризов.
//    • SignalR-интерфейсы используют nameof, не строки.
// ════════════════════════════════════════════════════════════════════

namespace Models.Commands.SignalR;
// ════════════════════════════════════════════════════════════════════
//  Capabilities — что клиент говорит о себе серверу
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Одна команда клиента, как её видит сервер и LLM.
/// Язык-независимая структура: все значения — коды.
/// </summary>
public sealed record CapabilityDto
{
    /// <summary>Идентификатор команды. Формат: lower.dot.case.</summary>
    public required CommandId Id { get; init; }

    /// <summary>Человекочитаемое имя. Если пусто — сервер сам хуманизирует.</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>Описание для LLM. Обязательно — берётся из [Description].</summary>
    public required string Description { get; init; }

    /// <summary>Первый сегмент Id в PascalCase: "audio.list" → "Audio".</summary>
    public string Category { get; init; } = "";

    /// <summary>Версия контракта. Из [AssemblyInformationalVersion].</summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>CLR-тип возврата. Для MCP structured output.</summary>
    public string ReturnType { get; init; } = "System.String";

    /// <summary>Параметры метода.</summary>
    public IReadOnlyList<ParameterDto> Parameters { get; init; } = Array.Empty<ParameterDto>();

    /// <summary>Примеры вызова. Помогают LLM.</summary>
    public IReadOnlyList<ExampleDto> Examples { get; init; } = Array.Empty<ExampleDto>();

    /// <summary>Возможные ошибки. Помогают LLM восстанавливаться.</summary>
    public IReadOnlyList<FailureDto> Failures { get; init; } = Array.Empty<FailureDto>();

    /// <summary>Свободные теги для фильтрации.</summary>
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
}

// ── Sync: клиент → сервер ───────────────────────────

// ════════════════════════════════════════════════════════════════════
//  Execution — вызов команды
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  Health — heartbeat
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  Configuration — push конфига от сервера
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  SignalR — типизированные контракты хабов
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  JSON — единые опции для всего стека
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  Использование — как выглядит поток
// ════════════════════════════════════════════════════════════════════
//
//  ── На клиенте ──
//
//    var caps = new CapabilityScanner().Scan(Assembly.GetExecutingAssembly());
//
//    var request = new CapabilitySyncRequest
//    {
//        Capabilities = caps,
//        ClientVersion = Assembly.GetEntryAssembly()!.GetName().Version!.ToString(),
//        MachineGuid = GetMachineGuid(),
//    };
//
//    var response = await _connection.InvokeAsync<CapabilitySyncResponse>(
//        nameof(IAgentHub.SyncCapabilities),
//        request);
//
//    // Подписка на вызовы:
//    _connection.On<CommandExecutionRequest, CommandExecutionResult>(
//        nameof(IAgentClient.ExecuteCommand),
//        async req => await _executor.ExecuteAsync(req, ct));
//
//    // Heartbeat:
//    await _connection.InvokeAsync(
//        nameof(IAgentHub.Heartbeat),
//        new HeartbeatReport { ClientId = _clientId, UptimeSeconds = uptime });
//
//  ── На сервере ──
//
//    public sealed class AgentHub : Hub<IAgentClient>, IAgentHub
//    {
//        public async Task<CapabilitySyncResponse> SyncCapabilities(CapabilitySyncRequest req)
//        {
//            var clientId = Context.GetClientId();
//            await _capabilities.ReplaceForClientAsync(clientId, req.Capabilities);
//            await _adminHub.Clients.All.CapabilitiesUpdated(clientId, req.Capabilities.Count);
//
//            return CapabilitySyncResponse.Ack(req.Capabilities.Count, clientId);
//        }
//
//        public Task Heartbeat(HeartbeatReport report)
//        {
//            _registry.Touch(report.ClientId);
//            return Task.CompletedTask;
//        }
//
//        public Task ReportExecution(CommandExecutionResult result)
//        {
//            return _adminHub.Clients.All.ExecutionCompleted(result);
//        }
//    }
//
//  ── Вызов команды с сервера ──
//
//    var agent = await _capabilities.FindClientAsync(commandId);
//    var result = await _hub.Clients
//        .Client(agent.ConnectionId)
//        .ExecuteCommand(new CommandExecutionRequest
//        {
//            ExecutionId = Guid.NewGuid(),
//            CommandId = commandId,
//            NamedArgs = new Dictionary<string, object?> { ["color"] = "#FF0000" },
//        });
//
// ════════════════════════════════════════════════════════════════════
//  Что НЕ описано в этом файле
// ════════════════════════════════════════════════════════════════════
//
//  • CapabilityScanner — рефлексия и эвристики. Отдельный файл.
//  • Hub реализация (AgentHub, AdminHub) — серверный проект.
//  • Client hub connection — клиентский проект.
//  • Persistence слой (EF Core, БД) — серверный проект.
//  • MCP converter (CapabilityDto → MCP tool descriptor) — отдельный файл.
//  • SignalR source generator для типизированных вызовов — если решим.
//
//  Всё это — следуюшие слои поверх Models.Contracts.
// ════════════════════════════════════════════════════════════════════