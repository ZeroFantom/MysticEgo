using Microsoft.AspNetCore.Mvc;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Extensions;

namespace Api.Auth.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected Guid? CurrentUserId => User.GetUserId();
    protected string? CurrentLogin => User.GetLogin();

    protected ActionResult ApiError(int status, string code, string message)
        => StatusCode(status, new ApiErrorDto
        {
            Code = code,
            Message = message,
            TraceId = HttpContext.TraceIdentifier,
        });
}