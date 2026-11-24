using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Agent.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/agent/software")]
public sealed class SoftwareController(IVendorInstallationService installer,NodeOptions node) : ControllerBase
{
    private bool LocalOwner()
    {
        if(!OperatingSystem.IsWindows()||User.Identity?.IsAuthenticated!=true||User.FindFirst("token_use")?.Value=="device")return false;
        if(HttpContext.Connection.RemoteIpAddress is not { } remote||!IPAddress.IsLoopback(remote))return false;
        var owner=Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER");
        return (!string.IsNullOrWhiteSpace(owner)&&User.Identity.Name==owner)||(node.DeploymentMode=="single-machine"&&User.IsInRole("system.developer"));
    }
    [HttpGet("catalog")]
    public IActionResult Catalog()=>LocalOwner()?Ok(installer.Catalog()):Forbid();
    [HttpGet("jobs")]
    public IActionResult Jobs()=>LocalOwner()?Ok(installer.Jobs()):Forbid();
    [HttpPost("install/{id}")]
    public IActionResult Install(string id)
    {
        if(!LocalOwner())return Forbid();
        var (status,result)=installer.Install(id);
        return status==202?Accepted("/api/v1/agent/software/jobs",result):StatusCode(status,result);
    }
}
