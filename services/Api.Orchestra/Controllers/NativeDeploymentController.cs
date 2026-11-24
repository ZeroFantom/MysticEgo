using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/orchestra/deployments/native")]
public sealed class NativeDeploymentController(INativeDeploymentPlanService plans,NativeDeploymentJobs jobs) : ControllerBase
{
    private bool Developer() => EcosystemHierarchy.IsDeveloper(User);
    [HttpGet("plans")]
    public IActionResult Plans() => Developer() ? Ok(plans.List()) : Forbid();
    [HttpGet("plans/{id}")]
    public async Task<IActionResult> Plan(string id,CancellationToken ct)
    {
        if(!Developer())return Forbid();
        try{var content=await plans.Read(id,ct);return content is null?NotFound():Content(content,"application/json");}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpPut("plans/{id}")]
    public async Task<IActionResult> Save(string id,[FromBody] NativePlan payload,CancellationToken ct)
    {
        if(!Developer())return Forbid();
        try{return Ok(await plans.Save(id,payload,ct));}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpDelete("plans/{id}")]
    public IActionResult Delete(string id)
    {
        if(!Developer())return Forbid();
        try{plans.Delete(id);return NoContent();}
        catch(NodeApiException ex){return StatusCode(ex.StatusCode,new{error=ex.Message});}
    }
    [HttpGet("jobs")]
    public IActionResult Jobs() => Developer()?Ok(jobs.List()):Forbid();
    [HttpGet("jobs/{id}")]
    public IActionResult Job(string id)
    {
        if(!Developer())return Forbid();
        var job=jobs.Find(id);return job is null?NotFound():Ok(job);
    }
    [HttpPost("plans/{id}/apply")]
    public IActionResult Apply(string id)
    {
        return StatusCode(410, new { error = "Используйте «Развёртывание Global» в панели Оркестра." });
    }
}
