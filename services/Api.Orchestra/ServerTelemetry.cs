using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;
namespace MysticEgo.Api.Orchestra;

[ApiController, Authorize, Route("api/v1/orchestra/telemetry")]
public sealed class TelemetryController(ServerTelemetry telemetry, ManagedNetwork network, NodeState state) : ControllerBase
{
    [HttpGet]
    public IActionResult Get() => EcosystemHierarchy.IsDeveloper(User)
        ? Ok(new { os = RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
            samples = telemetry.History(), audit = new { enrolled = network.Nodes().Count(node => node.Kind == "audit"),
                online = network.Nodes().Count(node => node.Kind == "audit" && network.Report(node.Id)?.Healthy.Contains("audit") == true) }, links = network.Nodes().Select(node => new
            {
                from = node.Id, name = node.Name, to = node.ParentId,
                parentName = network.Parent(node)?.Name ?? state.Options.DisplayName,
                online = network.Report(node.Id)?.Healthy.Contains(node.Kind) == true,
                measurement = network.Report(node.Id)?.Link
            }).ToArray(), observabilityAvailable = OperatingSystem.IsLinux() }) : Forbid();

    [HttpPost("monitoring")]
    public IActionResult StartMonitoring() => StatusCode(410, new
    {
        error = "Откройте «Диагностика → Развёртывание инфраструктуры» или установите через CloudDesk."
    });
}
