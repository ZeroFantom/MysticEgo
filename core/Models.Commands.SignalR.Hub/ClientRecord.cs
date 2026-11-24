// ════════════════════════════════════════════════════════════════════
//  Models.Commands.Server — единый файл
//  Серверная инфраструктура: реестр клиентов, хранилище capabilities,
//  диспетчер выполнения, базовые SignalR-хабы.
// ════════════════════════════════════════════════════════════════════
//
//  csproj:
//  <Project Sdk="Microsoft.NET.Sdk">
//    <PropertyGroup>
//      <TargetFramework>net10.0</TargetFramework>
//      <Nullable>enable</Nullable>
//      <ImplicitUsings>enable</ImplicitUsings>
//      <RootNamespace>Models.Commands.Server</RootNamespace>
//      <LangVersion>latest</LangVersion>
//    </PropertyGroup>
//
//    <ItemGroup>
//      <FrameworkReference Include="Microsoft.AspNetCore.App" />
//    </ItemGroup>
//
//    <ItemGroup>
//      <ProjectReference Include="..\Models.Commands\Models.Commands.csproj" />
//      <ProjectReference Include="..\Models.Identifiers\Models.Identifiers.csproj" />
//      <ProjectReference Include="..\Models.Contracts\Models.Contracts.csproj" />
//    </ItemGroup>
//  </Project>
//
//  ФИЛОСОФИЯ
//  ────────────────────────────────────────────────────────────────
//  • Хабы абстрактные — наследуй и расширяй.
//  • Все public-методы virtual — можешь переопределить целиком.
//  • Protected hooks — для добавления поведения без переопределения.
//  • Ошибки не улетают в SignalR-соединение — логируются, превращаются
//    в типизированные ответы или уведомления.
//  • Никаких magic strings — nameof + типизированные ID.
//  • Ни один метод не падает наружу.
// ════════════════════════════════════════════════════════════════════

namespace Models.Commands.SignalR.Hub;
// ════════════════════════════════════════════════════════════════════
//  ClientRecord — состояние подключённого клиента
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Снимок подключённого клиента. Иммутабельный, обновляется целиком.
/// Хранится в реестре. Содержит всё, что нужно для маршрутизации
/// и отображения в админке.
/// </summary>
public sealed record ClientRecord
{
    /// <summary>Стабильный ID клиента (guid из MachineGuid).</summary>
    public required Guid ClientId { get; init; }

    /// <summary>SignalR connection id. Меняется при реконнекте.</summary>
    public required string ConnectionId { get; init; }

    public string MachineName { get; init; } = "";
    public string IpAddress { get; init; } = "";
    public string Version { get; init; } = "0.0.0";

    /// <summary>Идентификатор аутентифицированного пользователя (если есть).</summary>
    public string? UserIdentifier { get; init; }

    public DateTime ConnectedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; init; } = DateTime.UtcNow;

    public int CapabilityCount { get; init; }
    public bool Online { get; init; } = true;

    /// <summary>Аптайм процесса на клиенте, секунды. Из последнего heartbeat.</summary>
    public long UptimeSeconds { get; init; }

    /// <summary>Сколько команд выполняется прямо сейчас. Из последнего heartbeat.</summary>
    public int RunningCommands { get; init; }

    public ClientSummaryDto ToSummary() => new()
    {
        ClientId = ClientId,
        MachineName = MachineName,
        IpAddress = IpAddress,
        Version = Version,
        CapabilityCount = CapabilityCount,
        Online = Online,
        LastSeen = LastSeenAt,
        ConnectionId = ConnectionId,
    };
}

// ════════════════════════════════════════════════════════════════════
//  CapabilityStore — хранилище capabilities клиентов
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  ExecutionDispatcher — отправка команд клиенту с ожиданием результата
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  HubErrorHandler — единая обработка ошибок в хабах
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  AgentHubBase — базовый хаб для клиентов
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  AdminHubBase — базовый хаб для админки
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  AgentNotifier — push capabilities из сервисов в админку
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  DI Extensions
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  Использование
// ════════════════════════════════════════════════════════════════════
//
//  ── Регистрация в Program.cs ──
//
//    builder.Services.AddCommandServerCore();
//    builder.Services.AddExecutionDispatcherFor<AgentHub>();
//    builder.Services.AddAdminNotifierFor<AdminHub>();
//    builder.Services.AddSignalR();
//
//    app.MapHub<AgentHub>("/agentHub");
//    app.MapHub<AdminHub>("/adminHub");
//
//  ── Наследование ──
//
//    public sealed class AgentHub : AgentHubBase
//    {
//        private readonly IAdminNotifier _notifier;
//
//        public AgentHub(
//            IClientRegistry registry,
//            ICapabilityStore capabilities,
//            IExecutionDispatcher dispatcher,
//            IClientIdProvider clientIdProvider,
//            IHubErrorHandler errorHandler,
//            IAdminNotifier notifier,
//            ILogger<AgentHub> logger)
//            : base(registry, capabilities, dispatcher, clientIdProvider, errorHandler, logger)
//        {
//            _notifier = notifier;
//        }
//
//        protected override async Task OnClientConnectedAsync(
//            ClientRecord record, CancellationToken ct)
//        {
//            await _notifier.ClientConnectedAsync(record.ToSummary(), ct);
//        }
//
//        protected override async Task OnClientDisconnectedAsync(
//            ClientRecord record, Exception? ex, CancellationToken ct)
//        {
//            await _notifier.ClientDisconnectedAsync(record.ClientId, ct);
//        }
//
//        protected override async Task OnCapabilitiesSyncedAsync(
//            Guid clientId, IReadOnlyList<CapabilityDto> caps, CancellationToken ct)
//        {
//            await _notifier.CapabilitiesUpdatedAsync(clientId, caps.Count, ct);
//        }
//
//        // Дополнительно можно переопределить SyncCapabilities,
//        // если нужна особая логика валидации:
//        // public override async Task<CapabilitySyncResponse> SyncCapabilities(...)
//        // {
//        //     // ... валидация
//        //     return await base.SyncCapabilities(request);
//        // }
//    }
//
//    public sealed class AdminHub : AdminHubBase
//    {
//        public AdminHub(
//            IClientRegistry registry,
//            ICapabilityStore capabilities,
//            IExecutionDispatcher dispatcher,
//            IHubErrorHandler errorHandler,
//            ILogger<AdminHub> logger)
//            : base(registry, capabilities, dispatcher, errorHandler, logger)
//        { }
//
//        // Таймаут по умолчанию для выполнения через админку
//        protected override TimeSpan DefaultExecutionTimeout => TimeSpan.FromMinutes(2);
//    }
//
//  ── Вызов команды из сервиса ──
//
//    public sealed class AgentCommandService
//    {
//        private readonly IExecutionDispatcher _dispatcher;
//        private readonly IAdminNotifier _notifier;
//
//        public async Task<CommandExecutionResult> RunAsync(
//            CommandExecutionRequest req, CancellationToken ct)
//        {
//            var result = await _dispatcher.DispatchToAnyAsync(
//                req, TimeSpan.FromSeconds(30), ct);
//
//            await _notifier.ExecutionCompletedAsync(result, ct);
//            return result;
//        }
//    }
//
// ════════════════════════════════════════════════════════════════════
//  Ключевые идеи
//  ────────────────────────────────────────────────────────────────
//  1. Хабы — абстрактные. Ты всегда работаешь через свой наследник.
//  2. Все операции — с полным try/catch, ошибки не улетают в соединение.
//  3. Hooks позволяют добавить логику без переопределения public-методов.
//  4. IAdminNotifier — единый канал push'а в админку.
//  5. IExecutionDispatcher — единый канал вызова команд,
//     знает про timeout, ожидание и очистку pending.
//  6. ClientIdProvider — точка кастомизации, как ты достаёшь id клиента
//     из HubCallerContext.
// ════════════════════════════════════════════════════════════════════