using Models.Commands;
using Models.Commands.Scanner;
using Models.Commands.SignalR.Clients;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
namespace MysticEgo.Distributed;
public sealed class ClusterAgentClient : AgentHubClientBase
{
    public ClusterAgentClient(AgentHubClientOptions options,ICapabilityScanner scanner,ICommandInvoker invoker,ILogger<ClusterAgentClient> logger) : base(options,scanner,invoker,logger){}
}
[CommandContract("system.info",1)]
[CommandDescription("Read operating system, host and process information")]
public sealed class SystemInfoCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)=>Task.FromResult(CommandResult.Ok(structured:new { host=Environment.MachineName,os=RuntimeInformation.OSDescription,platform=Environment.OSVersion.Platform.ToString(),processors=Environment.ProcessorCount,uptime=Environment.TickCount64,version=Environment.Version.ToString()}));
}
[CommandContract("process.list",1)]
[CommandDescription("List running processes with their IDs and names")]
public sealed class ProcessListCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var processes=Process.GetProcesses().Select(p=> { try {return new {id=p.Id,name=p.ProcessName};} catch {return null;} }).Where(x=>x is not null).OrderBy(x=>x!.name).Take(500).ToArray();
        return Task.FromResult(CommandResult.Ok(structured:processes));
    }
}
[CommandContract("system.drives",1)]
[CommandDescription("Read local storage volumes and free capacity")]
public sealed class DriveListCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var disks=DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>new {name=d.Name,total=d.TotalSize,free=d.AvailableFreeSpace,format=d.DriveFormat}).ToArray();
        return Task.FromResult(CommandResult.Ok(structured:disks));
    }
}
