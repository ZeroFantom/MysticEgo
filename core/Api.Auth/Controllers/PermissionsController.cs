using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Autorization.Services;

namespace Api.Auth.Controllers;

/// <summary>
/// Каталог прав и инструментов.
/// </summary>
[ApiController]
[Route("api/permissions")]
[Authorize]
[Produces("application/json")]
public sealed class PermissionsController : BaseApiController
{
    private readonly IPermissionService _perms;

    public PermissionsController(IPermissionService perms) => _perms = perms;

    /// <summary>
    /// Все права с инструментами.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
        => Ok(await _perms.ListAsync(ct));

    /// <summary>
    /// Право по Id.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var dto = await _perms.GetAsync(id, ct);
        if (dto is null)
            return ApiError(404, "permission_not_found", $"Право {id} не найдено.");

        return Ok(dto);
    }

    /// <summary>
    /// Инструменты, входящие в право.
    /// </summary>
    [HttpGet("{id:int}/tools")]
    public async Task<IActionResult> Tools(int id, CancellationToken ct)
        => Ok(await _perms.ListToolsAsync(id, ct));
}