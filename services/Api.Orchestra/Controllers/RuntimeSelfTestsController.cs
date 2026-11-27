using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MysticEgo.Api.Orchestra.Controllers;

/// <summary>Owner-only live self-diagnostics and safe exports. No arbitrary execution endpoint.</summary>
[ApiController,Authorize(Policy="operator"),Route("api/v1/orchestra/self-tests")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class RuntimeSelfTestsController(RuntimeSelfTests tests,DevelopmentTeam team):ControllerBase
{
    [HttpGet]
    public IActionResult Status()=>!team.IsOwner(User)?Forbid():Ok(new{latest=tests.Latest(),history=tests.History().Select(r=>new{r.At,passed=r.Checks.Count(c=>c.Result=="pass"),failed=r.Checks.Count(c=>c.Result=="fail"),unknown=r.Checks.Count(c=>c.Result=="unknown"),skipped=r.Checks.Count(c=>c.Result=="skip")})});
    [HttpGet("report")]
    public IActionResult Report([FromQuery]string format="xlsx")
    {
        if(!team.IsOwner(User))return Forbid();
        if(format is not ("xlsx" or "json" or "txt"))return BadRequest();
        var runs=tests.History();
        DiagnosticReport.Table[] tables=[
            new("Summary",["field","value"],[["generatedUtc",DateTimeOffset.UtcNow],["scope","Read-only runtime checks, not full CI"],["runs",runs.Length]]),
            new("Checks",["runUtc","test","category","result","reason","observedUtc","latencyMs"],runs.SelectMany(r=>r.Checks.Select(c=>new object?[]{r.At,c.Name,c.Category,c.Result,c.Reason,c.At,c.LatencyMs})).ToArray())
        ];
        var mime=format=="xlsx"?"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet":format=="json"?"application/json":"text/plain";
        return File(DiagnosticReport.Encode(tables,format),mime,"MysticEgo-self-tests-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")+"Z."+format);
    }
}
