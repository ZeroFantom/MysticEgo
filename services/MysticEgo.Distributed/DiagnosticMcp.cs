using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Models.Commands.Mcp;
using Models.Commands.SignalR;
using Models.Commands.SignalR.Hub;

namespace MysticEgo.Distributed;

public sealed class DiagnosticMcpService(IMcpDispatcher dispatcher, ICapabilityStore capabilities, INodeCommandService commands,
    IAssistantProposalStore proposals, NodeOptions node, IConfiguration config)
{
    private static readonly HashSet<string> ReadOnlyTools = ["system.info", "system.health", "system.drives", "rgb.status", "integrations.status"];
    private bool Desktop => node.Kind is NodeKind.Agent or NodeKind.AuditOrchestra;
    public async Task<JsonRpcResponse> Dispatch(JsonRpcRequest req, string actor, bool localConsole, CancellationToken ct)
    {
        if (req.Method == "tools/call")
        {
            var result = new JsonRpcResponse { Id = req.Id };
            if (req.Params is not { ValueKind: JsonValueKind.Object } parameters ||
                !parameters.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String)
            {
                result.Result = McpCallToolResult.Fail("Укажите название инструмента."); return result;
            }
            parameters.TryGetProperty("arguments", out var args);
            var tool = name.GetString()!;
            result.Result = await Call(tool, args, actor, localConsole, ct);
            return result;
        }
        var response = await dispatcher.DispatchAsync(req, ct);
        if (response.Result is McpListToolsResult list)
        {
            var dynamic = Desktop ? new List<McpToolDescriptor>() : list.Tools.Where(t => ReadOnlyTools.Contains(t.Name)).DistinctBy(t => t.Name).ToList();
            foreach (var tool in dynamic)
            {
                tool.InputSchema.Properties["targetId"] = new McpPropertySchema { Type = "string", Format = "uuid", Description = "ClientId из /api/v1/agents" };
                tool.InputSchema.Required.Add("targetId"); tool.Annotations.ReadOnlyHint = true;
            }
            dynamic.Add(BuiltIn("node.status", "Текущий узел, ОС, память процесса и время работы", true));
            if (node.Kind is NodeKind.Agent or NodeKind.Local)
                dynamic.Add(BuiltIn("club.info", "Описание и адрес клуба из конфигурации Local", true));
            if (Desktop && localConsole)
            {
                dynamic.Add(BuiltIn("desktop.windows", "Заголовки видимых окон текущего сеанса", true));
                dynamic.Add(BuiltIn("desktop.launch.propose", "Создать предложение запустить ранее разрешённое приложение", false, "alias"));
            }
            list.Tools = dynamic;
        }
        return response;
    }

    private static McpToolDescriptor BuiltIn(string name, string label, bool readOnly, string? argument = null)
    {
        var tool = new McpToolDescriptor { Name = name, Description = label };
        tool.Annotations.ReadOnlyHint = readOnly;
        if (argument is not null)
        {
            tool.InputSchema.Properties[argument] = new McpPropertySchema { Type = "string", Description = "Псевдоним из AuditDesktop" };
            tool.InputSchema.Required.Add(argument);
        }
        return tool;
    }

    private async Task<McpCallToolResult> Call(string tool, JsonElement args, string actor, bool localConsole, CancellationToken ct)
    {
        if (tool == "node.status")
        {
            using var process = Process.GetCurrentProcess();
            return Output(new { node.NodeId, node.DisplayName, kind = node.Kind.ToString(), os = Environment.OSVersion.ToString(),
                memory = process.WorkingSet64, uptime = Environment.TickCount64 });
        }
        if (tool == "club.info" && node.Kind is NodeKind.Agent or NodeKind.Local)
            return Output(new { node.ClubAddress, node.ClubDescription, node.LocalId, node.GlobalId });
        if (Desktop && localConsole && tool == "desktop.launch.propose")
        {
            if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("alias", out var value) || value.ValueKind != JsonValueKind.String ||
                !System.Text.RegularExpressions.Regex.IsMatch(value.GetString()!, "^[A-Za-z0-9_.-]{1,40}$"))
                return McpCallToolResult.Fail("Укажите разрешённый псевдоним приложения.");
            var items = proposals.Create(actor, [("launch_application", value.GetString()!)]);
            return Output(new { proposals = items, confirmation = "/api/v1/assistant/workbench/proposals/{id}/confirm" });
        }
        if (Desktop && localConsole && tool == "desktop.windows") return await ReadWindows(ct);
        if (Desktop || !ReadOnlyTools.Contains(tool) || args.ValueKind != JsonValueKind.Object ||
            !args.TryGetProperty("targetId", out var id) || id.ValueKind != JsonValueKind.String || !id.TryGetGuid(out var target) ||
            !(await capabilities.GetAsync(target, ct)).Any(c => c.Id.Value == tool))
            return McpCallToolResult.Fail("Выберите доступный инструмент. Для агентской диагностики укажите targetId.");
        var run = (CommandExecutionResult)await commands.Dispatch(new(target, tool, "", null, 15), actor, ct);
        return run.Success ? run.StructuredOutput is null ? McpCallToolResult.Ok(run.Output ?? "OK") : Output(run.StructuredOutput)
            : McpCallToolResult.Fail(run.ErrorMessage ?? "Ошибка диагностики");
    }

    private async Task<McpCallToolResult> ReadWindows(CancellationToken ct)
    {
        var root = config["Managed:PackageRoot"];
        if (!Environment.UserInteractive || root is null || Environment.GetEnvironmentVariable("MYSTICEGO_DOTNET") is not { } dotnet)
            return McpCallToolResult.Fail("Откройте AuditDesktop в интерактивном рабочем сеансе.");
        var converter = Path.Combine(root, "runtime", "app", "bin", "MysticEgo.DesktopGeometry.dll");
        if (!File.Exists(converter)) return McpCallToolResult.Fail("Нет компонента чтения окон.");
        var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { converter, "--json" }) start.ArgumentList.Add(arg);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(4));
        try
        {
            using var child = Process.Start(start)!;
            using var kill = timeout.Token.Register(() => { try { if (!child.HasExited) child.Kill(true); } catch (InvalidOperationException) { } });
            var text = await child.StandardOutput.ReadToEndAsync(timeout.Token);
            await child.WaitForExitAsync(timeout.Token);
            return child.ExitCode == 0 && text.Length <= 250_000 ? McpCallToolResult.Ok(text) :
                McpCallToolResult.Fail("Положение окон недоступно в этом сеансе.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is OperationCanceledException && !ct.IsCancellationRequested)
        { return McpCallToolResult.Fail("Окна текущего сеанса недоступны."); }
    }

    private static McpCallToolResult Output(object value) => McpCallToolResult.Ok(JsonSerializer.Serialize(value));
}

[ApiController, Authorize(Policy = "operator"), Route("mcp")]
public sealed class DiagnosticsMcpController(NodeOptions node, INodeAccessPolicy access, IAssistantAccessPolicy assistant, DiagnosticMcpService service) : ControllerBase
{
    [HttpPost, RequestSizeLimit(32768)]
    public async Task<IActionResult> Post(JsonRpcRequest request, CancellationToken ct)
    {
        var actor = assistant.ResolveOwner(User);
        if (actor is null || node.Kind == NodeKind.Orchestra && !EcosystemHierarchy.IsDeveloper(User) ||
            node.Kind is NodeKind.Global or NodeKind.Local && !access.CanCommand(User)) return Forbid();
        if (request.JsonRpc != "2.0" || string.IsNullOrWhiteSpace(request.Method)) return BadRequest();
        if (request.Method.StartsWith("notifications/", StringComparison.Ordinal) && request.Id is null) return Accepted();
        // Window titles and app proposals are confined to the device owner and a local client.
        var owner = Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");
        var localConsole = owner == actor && HttpContext.Connection.RemoteIpAddress is { } address && IPAddress.IsLoopback(address);
        return Ok(await service.Dispatch(request, actor, localConsole, ct));
    }
}
