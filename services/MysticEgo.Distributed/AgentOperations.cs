using Models.Commands;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace MysticEgo.Distributed;

[CommandContract("network.interfaces",1)]
[CommandDescription("Read network adapters, addresses and interface speeds")]
public sealed class NetworkInterfacesCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var adapters=NetworkInterface.GetAllNetworkInterfaces().Select(x=>new
        {
            x.Id,x.Name,x.Description,status=x.OperationalStatus.ToString(),speedBitsPerSecond=x.Speed,
            ips=x.GetIPProperties().UnicastAddresses.Select(a=>a.Address.ToString()).ToArray()
        }).ToArray();
        return Task.FromResult(CommandResult.Ok(structured:adapters));
    }
}

[CommandContract("system.health",1)]
[CommandDescription("Read machine runtime and memory telemetry")]
public sealed class SystemHealthCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        using var proc=Process.GetCurrentProcess();
        return Task.FromResult(CommandResult.Ok(structured:new
        {
            host=Environment.MachineName,
            uptimeMs=Environment.TickCount64,
            managedHeapBytes=GC.GetTotalMemory(false),
            agentWorkingSetBytes=proc.WorkingSet64,
            agentThreads=proc.Threads.Count,
            operatingSystem=RuntimeInformation.OSDescription,
            timestampUtc=DateTimeOffset.UtcNow
        }));
    }
}

/// <summary>
/// Разрешённый список процессов задаёт владелец машины. Запуск произвольных команд оболочки запрещён.
/// Модуль предназначен для управляемых рабочих станций, а не произвольных компьютеров.
/// </summary>
[CommandContract("process.terminate",1)]
[CommandArgument("pid", "Идентификатор процесса", Type = typeof(int), Example = "1234")]
[CommandDescription("Terminate an allowlisted process by PID; disabled by default")]
[CommandRisk(CommandRisk.Privileged,true,"node.command")]
public sealed class TerminateAllowlistedProcessCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var allow=(Environment.GetEnvironmentVariable("MYSTICEGO_TERMINABLE_PROCESSES")??"").Split(';',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(allow.Count==0)return Task.FromResult(CommandResult.Fail("disabled","Termination is disabled. Configure an explicit process allowlist."));
        if(!int.TryParse(ctx.RawArgs?.Trim(),out var pid)||pid<1||pid==Environment.ProcessId)return Task.FromResult(CommandResult.Fail("invalid_pid","Supply a valid other process ID."));
        try
        {
            using var target=Process.GetProcessById(pid);
            if(!allow.Contains(target.ProcessName))return Task.FromResult(CommandResult.Fail("not_allowlisted","Process is not allowlisted on this machine."));
            ct.ThrowIfCancellationRequested();
            target.Kill(entireProcessTree:false);
            return Task.FromResult(CommandResult.Ok($"Termination requested for {target.ProcessName} PID {pid}"));
        }
        catch(Exception e) when(e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {return Task.FromResult(CommandResult.Fail("terminate_failed",e.Message));}
    }
}
