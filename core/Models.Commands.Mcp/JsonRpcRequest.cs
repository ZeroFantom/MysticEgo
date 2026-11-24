// ════════════════════════════════════════════════════════════════════
//  Models.Commands.Server.Mcp — единый файл
//  MCP-фасад над capability-системой.
//
//  Что делает:
//    • tools/list  — собирает capabilities всех онлайн-клиентов
//                    и отдаёт их как MCP tools.
//    • tools/call  — находит клиента, отправляет CommandExecutionRequest
//                    через IExecutionDispatcher, возвращает результат.
//    • resources/* — заглушки на будущее.
//
//  От старого McpServer (Models.Commands.AI.McpServer) отличается тем,
//  что НЕ ходит в локальный реестр команд. Источник истины — клиенты,
//  которые сами присылают свои capabilities.
//
//  csproj:
//  <Project Sdk="Microsoft.NET.Sdk">
//    <PropertyGroup>
//      <TargetFramework>net10.0</TargetFramework>
//      <Nullable>enable</Nullable>
//      <ImplicitUsings>enable</ImplicitUsings>
//      <RootNamespace>Models.Commands.Server.Mcp</RootNamespace>
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
//      <ProjectReference Include="..\Models.Commands.Server\Models.Commands.Server.csproj" />
//    </ItemGroup>
//  </Project>
// ════════════════════════════════════════════════════════════════════

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Models.Commands.Mcp;
// ════════════════════════════════════════════════════════════════════
//  §1  MCP CONTRACTS — JSON-RPC
// ════════════════════════════════════════════════════════════════════

public sealed class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")] public string JsonRpc { get; set; } = "2.0";
    [JsonPropertyName("id")] public JsonElement? Id { get; set; }
    [JsonPropertyName("method")] public string Method { get; set; } = "";
    [JsonPropertyName("params")] public JsonElement? Params { get; set; }
}

// ════════════════════════════════════════════════════════════════════
//  §2  MCP CONTRACTS — payload
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  §3  MCP CONTRACTS — tool descriptor
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  §4  McpConverter — CapabilityDto → McpToolDescriptor
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  §5  McpToolExecutor — tools/call → IExecutionDispatcher
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  §6  McpDispatcher — JSON-RPC роутер
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  §7  McpEndpoints — HTTP /mcp
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  §8  DI extensions
// ════════════════════════════════════════════════════════════════════