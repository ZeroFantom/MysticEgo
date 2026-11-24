using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Commands;
using MysticEgo.Distributed;

namespace MysticEgo.Api.AuditOrchestra.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/audit-orchestra/onboarding")]
public sealed class AuditOnboardingController(IHomeAssessmentService assessments) : ControllerBase
{
    private string? Owner()
    {
        var configured=Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");
        var actual=User.Identity?.Name;
        return User.Identity?.IsAuthenticated==true && User.FindFirst("token_use")?.Value!="device" &&
            !string.IsNullOrWhiteSpace(configured) && string.Equals(configured,actual,StringComparison.Ordinal) ? actual : null;
    }
    [HttpGet("me")]
    [ApiCapability("audit.assessment.read","Прочитать добровольную самооценку владельца","home")]
    public async Task<IActionResult> Me(CancellationToken ct) => Owner() is { } actor ? Ok(await assessments.Get(actor,ct)) : Forbid();

    [HttpPut("me")]
    [ApiCapability("audit.assessment.update","Сохранить самооценку и достижения","home",RequiresConfirmation=true)]
    public async Task<IActionResult> Update([FromBody] HomeAssessmentEdit request,CancellationToken ct)
    {
        if(Owner() is not { } actor)return Forbid();
        try{return Ok(await assessments.Save(actor,request,ct));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
}
