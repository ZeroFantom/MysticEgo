using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models.Commands.Mcp;

namespace MysticEgo.Api.Orchestra;

/// <summary>Read-only MCP transport for the same cached capacity and reserve services as the HTTP dashboard.
/// Models cannot activate resources, change budgets or obtain provider credentials.</summary>
[ApiController,Authorize(Policy="operator"),Route("api/v1/orchestra/capacity/mcp")]
public sealed class CapacityMcpController(IMcpDispatcher dispatcher,CapacityMonitor monitor,CapacityReserve reserve,DevelopmentTeam team):ControllerBase
{
    [HttpPost,RequestSizeLimit(16384)]
    public async Task<IActionResult> Post(JsonRpcRequest request,CancellationToken ct)
    {
        if(!team.IsOwner(User))return Forbid();
        if(request.JsonRpc!="2.0"||string.IsNullOrWhiteSpace(request.Method))return BadRequest();
        if(request.Method.StartsWith("notifications/",StringComparison.Ordinal)&&request.Id is null)return Accepted();
        if(request.Method=="tools/call")
        {
            var result=new JsonRpcResponse{Id=request.Id};
            if(request.Params is not {ValueKind:JsonValueKind.Object} args||
                !args.TryGetProperty("name",out var tool)||tool.ValueKind!=JsonValueKind.String)
            {result.Result=McpCallToolResult.Fail("Tool name required.");return Ok(result);}
            object? output=tool.GetString() switch
            {
                "capacity.status"=>monitor.Read()?.Assessment,
                "capacity.reserve_plan"=>reserve.Plan(),
                _=>null
            };
            result.Result=output is null?McpCallToolResult.Fail("Unknown tool or capacity not yet measured."):
                McpCallToolResult.Ok(JsonSerializer.Serialize(output,new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            return Ok(result);
        }
        if(request.Method is not ("initialize" or "ping" or "tools/list"))return BadRequest();
        var response=await dispatcher.DispatchAsync(request,ct);
        if(response.Result is McpListToolsResult list)
        {
            var status=new McpToolDescriptor{Name="capacity.status",Description="Read latest measured capacity, pressure and cost estimate."};
            var plan=new McpToolDescriptor{Name="capacity.reserve_plan",Description="Read budgeted standby eligibility without changing infrastructure."};
            status.Annotations.ReadOnlyHint=true;plan.Annotations.ReadOnlyHint=true;
            list.Tools=[status,plan];
        }
        return Ok(response);
    }
}
