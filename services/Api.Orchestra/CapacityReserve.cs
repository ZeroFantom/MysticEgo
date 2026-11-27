using System.Net;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

/// <summary>Start a pre-provisioned, explicitly bound standby. No create/delete or automatic billing operation.
/// A separate readiness and shared-state check is required before admitting it to a load balancer.</summary>
public sealed class CapacityReserve(CapacityMonitor monitor,TimewebApiClient cloud,IConfiguration config,NodeState state)
{
    private readonly SemaphoreSlim gate=new(1,1);
    public object Plan()
    {
        var report=monitor.Read()?.Assessment;
        var configured=Binding() is not null;
        return new{configured,requiresConfirmation=true,mode="start-existing-standby",
            eligible=configured&&report is {AdditionalPools:>0,WithinBudget:true}&&DateTimeOffset.UtcNow-report.At<TimeSpan.FromSeconds(20),
            reason="A fresh positive recommendation and a complete configured budget are required. Starting does not join the load balancer."};
    }
    private (int Server,IPAddress Ip)? Binding()
    {
        if(!config.GetValue("Capacity:Reserve:Enabled",false)||
            !int.TryParse(config["Capacity:Reserve:ServerId"],out var server)||server<=0||
            !int.TryParse(config["Timeweb:OrchestraServerId"],out var primary)||primary<=0||server==primary||
            !IPAddress.TryParse(config["Capacity:Reserve:Ipv4"],out var ip)||ip.AddressFamily!=System.Net.Sockets.AddressFamily.InterNetwork||
            string.IsNullOrWhiteSpace(config["Timeweb:Token"]))return null;
        return (server,ip);
    }
    public async Task<(int Status,object Body)> Start(string actor,bool confirmed,CancellationToken ct)
    {
        if(!confirmed||Binding() is not { } bind)return (409,new{code="reserve_not_configured_or_confirmed"});
        if(!await gate.WaitAsync(0,ct))return (409,new{code="operation_in_progress"});
        try
        {
            var report=monitor.Read()?.Assessment;
            if(report is not {AdditionalPools:>0,WithinBudget:true}||DateTimeOffset.UtcNow-report.At>TimeSpan.FromSeconds(20))
                return (409,new{code="fresh_budgeted_recommendation_required"});
            using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(12));
            var ips=await cloud.Get($"servers/{bind.Server}/ips",deadline.Token);
            if(ips?["server_ips"] is not JsonArray list||
                !list.OfType<JsonObject>().Any(x=>TimewebApiClient.Value(x,"type")=="ipv4"&&TimewebApiClient.Value(x,"ip")==bind.Ip.ToString()))
                return (409,new{code="reserve_identity_mismatch"});
            var data=await cloud.Get($"servers/{bind.Server}",deadline.Token);
            if(data?["server"] is not JsonObject server||TimewebApiClient.Value(server,"id")!=bind.Server.ToString())
                return (502,new{code="unexpected_provider_identity"});
            var current=TimewebApiClient.Value(server,"status");
            if(current=="on")return (200,new{status="already_on",serverId=bind.Server,ready=false});
            if(current!="off")return (409,new{code="reserve_not_stopped"});
            using var result=await cloud.Send(HttpMethod.Post,$"servers/{bind.Server}/start",deadline.Token);
            result.EnsureSuccessStatusCode();
            state.Log(actor,"capacity.reserve.start",bind.Server.ToString(),true);
            return (202,new{status="start_requested",serverId=bind.Server,ready=false});
        }
        catch(Exception ex) when(ex is HttpRequestException or System.Text.Json.JsonException or OperationCanceledException&&!ct.IsCancellationRequested)
        {return (502,new{code="provider_status_uncertain",message="Reconcile server status before retrying; no automatic retry."});}
        finally{gate.Release();}
    }
}

[ApiController,Authorize(Policy="operator"),Route("api/v1/orchestra/capacity/reserve")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class CapacityReserveController(CapacityReserve service,DevelopmentTeam team):ControllerBase
{
    public sealed record StartRequest(bool Confirmed);
    [HttpGet]public IActionResult Plan()=>team.IsOwner(User)?Ok(service.Plan()):Forbid();
    [HttpPost("start"),RequestSizeLimit(1024)]
    public async Task<IActionResult> Start(StartRequest request,CancellationToken ct)
    {
        if(!team.IsOwner(User))return Forbid();
        var result=await service.Start(User.Identity?.Name??"owner",request.Confirmed,ct);
        return StatusCode(result.Status,result.Body);
    }
}
