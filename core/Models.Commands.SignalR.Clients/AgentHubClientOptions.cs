// ════════════════════════════════════════════════════════════════════
//  Models.Commands.Client.Runtime — единый файл
//  Клиентская часть: подключение к хабу, авто-sync capabilities,
//  авто-heartbeat, выполнение входящих команд, прогресс.
// ════════════════════════════════════════════════════════════════════
//
//  csproj:
//  <Project Sdk="Microsoft.NET.Sdk">
//    <PropertyGroup>
//      <TargetFramework>net10.0</TargetFramework>
//      <Nullable>enable</Nullable>
//      <ImplicitUsings>enable</ImplicitUsings>
//      <RootNamespace>Models.Commands.Client.Runtime</RootNamespace>
//      <LangVersion>latest</LangVersion>
//    </PropertyGroup>
//
//    <ItemGroup>
//      <FrameworkReference Include="Microsoft.AspNetCore.App" />
//    </ItemGroup>
//
//    <ItemGroup>
//      <PackageReference Include="Microsoft.AspNetCore.SignalR.Client" Version="10.0.13" />
//    </ItemGroup>
//
//    <ItemGroup>
//      <ProjectReference Include="..\Models.Commands\Models.Commands.csproj" />
//      <ProjectReference Include="..\Models.Identifiers\Models.Identifiers.csproj" />
//      <ProjectReference Include="..\Models.Contracts\Models.Contracts.csproj" />
//      <ProjectReference Include="..\Models.Commands.Client\Models.Commands.Client.csproj" />
//    </ItemGroup>
//  </Project>
// ════════════════════════════════════════════════════════════════════

using System.Reflection;

namespace Models.Commands.SignalR.Clients;
// ════════════════════════════════════════════════════════════════════
//  Options
// ════════════════════════════════════════════════════════════════════

public sealed class AgentHubClientOptions
{
    /// <summary>URL хаба. Например: "http://localhost:5000/agentHub".</summary>
    public string HubUrl { get; set; } = "";

    /// <summary>API-токен. Прилетает в заголовке X-Api-Token.</summary>
    public string? ApiToken { get; set; }
    public Func<CancellationToken,Task<string>>? AccessTokenProvider { get; set; }

    /// <summary>Стабильный идентификатор клиента (MachineGuid).</summary>
    public Guid ClientId { get; set; } = Guid.Empty;

    /// <summary>Имя машины для отображения.</summary>
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>Интервал heartbeat.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Таймаут выполнения одной команды по умолчанию.</summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Автоматически синхронизировать capabilities при подключении.</summary>
    public bool AutoSyncCapabilities { get; set; } = true;

    /// <summary>Автоматически запускать heartbeat-цикл.</summary>
    public bool AutoHeartbeat { get; set; } = true;

    /// <summary>Таймаут установления соединения.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Сборки, в которых ищутся команды. По умолчанию — entry assembly.</summary>
    public Assembly[]? CommandAssemblies { get; set; }

    /// <summary>Дополнительные заголовки при подключении.</summary>
    public Dictionary<string, string> AdditionalHeaders { get; set; } = new();
}

// ════════════════════════════════════════════════════════════════════
//  IClientCommandRegistry — CommandId → Type
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  ICommandInvoker — выполнение команды по CommandContext
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  HeartbeatService — фоновый heartbeat
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  AgentHubClientBase — основной клиент
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  DI Extensions
// ════════════════════════════════════════════════════════════════════