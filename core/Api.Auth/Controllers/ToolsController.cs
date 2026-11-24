using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Autorization.Contracts.Requests;
using Models.Autorization.Discovery;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>
/// Управление инструментами и ре-сканирование discovery.
/// </summary>
[ApiController]
[Route("api/tools")]
[Authorize]
[Produces("application/json")]
public sealed class ToolsController : BaseApiController
{
    private readonly IToolService _tools;
    private readonly IToolDiscoveryService _discovery;
    private readonly ILogger<ToolsController> _log;

    public ToolsController(
        IToolService tools,
        IToolDiscoveryService discovery,
        ILogger<ToolsController> log)
    {
        _tools = tools;
        _discovery = discovery;
        _log = log;
    }

    /// <summary>
    /// Список всех инструментов.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _tools.ListAsync(ct));

    /// <summary>
    /// Включить или выключить инструмент.
    /// </summary>
    [HttpPatch("{id:int}/enabled")]
    public async Task<IActionResult> SetEnabled(
        int id, [FromBody] ToggleToolRequest req, CancellationToken ct)
    {
        var ok = await _tools.SetEnabledAsync(id, req.IsEnabled, ct);
        return ok ? Ok(new { success = true }) : NotFound();
    }

    /// <summary>
    /// Принудительно пере-сканировать контроллеры.
    /// Идемпотентно: существующие обновляются, новые добавляются,
    /// исчезнувшие — удаляются.
    /// </summary>
    [HttpPost("resync")]
    public async Task<IActionResult> Resync(CancellationToken ct)
    {
        var result = await _discovery.SyncAsync(ct);
        _log.LogInformation("Manual resync: {@Result}", result);
        return Ok(result);
    }
}