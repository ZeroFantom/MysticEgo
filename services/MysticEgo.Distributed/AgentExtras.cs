using Models.Commands;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
namespace MysticEgo.Distributed;
/// <summary>Инвентаризация без привязки к клубу и без доступа к персональным файлам.</summary>
[CommandContract("system.inventory",1)]
[CommandDescription("Сводка оборудования, дисков и сетевых интерфейсов")]
public sealed class SystemInventoryCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var drives=DriveInfo.GetDrives().Where(x=>x.IsReady).Select(x=>new {x.Name,x.TotalSize,x.AvailableFreeSpace}).ToArray();
        var nics=NetworkInterface.GetAllNetworkInterfaces().Select(x=>new {x.Name,Status=x.OperationalStatus.ToString(),Speed=x.Speed}).ToArray();
        return Task.FromResult(CommandResult.Ok(structured:new {host=Environment.MachineName,os=RuntimeInformation.OSDescription,cores=Environment.ProcessorCount,drives,nics,at=DateTimeOffset.UtcNow}));
    }
}
/// <summary>Перечень аудиоустройств Windows. Для остальных ОС возвращается явный отказ.</summary>
[CommandContract("audio.devices",1)]
[CommandDescription("Показать аудиоустройства Windows через системный PnP")]
public sealed class AudioDevicesCommand : ICommand
{
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        if(!OperatingSystem.IsWindows())return CommandResult.Fail("unsupported_os","Список PnP аудиоустройств доступен только на Windows.");
        try
        {
            using var proc=new Process();proc.StartInfo=new ProcessStartInfo("powershell.exe") {UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true};
            foreach(var arg in new[]{"-NoProfile","-NonInteractive","-Command","Get-PnpDevice -Class AudioEndpoint | Select-Object FriendlyName,Status,InstanceId | ConvertTo-Json -Compress"})proc.StartInfo.ArgumentList.Add(arg);
            proc.Start();using var registration=ct.Register(()=>{try{if(!proc.HasExited)proc.Kill();}catch{}});
            var stdout=await proc.StandardOutput.ReadToEndAsync(ct);await proc.WaitForExitAsync(ct);
            if(proc.ExitCode!=0)return CommandResult.Fail("pnp_failed","Не удалось получить список аудиоустройств через PowerShell.");
            return CommandResult.Ok(stdout.Length>150000?stdout[..150000]:stdout);
        }
        catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException) {return CommandResult.Fail("pnp_failed",ex.Message);}
    }
}
/// <summary>Диагностика наличия OpenRGB без скрытой установки стороннего ПО.</summary>
[CommandContract("rgb.status",1)]
[CommandDescription("Проверить, запущен ли OpenRGB")]
public sealed class RgbStatusCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var p=Process.GetProcessesByName("OpenRGB");try{return Task.FromResult(CommandResult.Ok(structured:new {available=p.Length>0,processes=p.Length,controlImplemented=false,note="Управление RGB требует переноса драйверного адаптера."}));}
        finally{foreach(var proc in p)proc.Dispose();}
    }
}

/// <summary>Установка цвета через уже установленный OpenRGB CLI. Никакой автоматической загрузки исполняемых файлов.</summary>
[CommandContract("rgb.set",1)]
[CommandArgument("color", "Цвет подсветки в формате HEX", Format = "color", Default = "#8B5CF6")]
[CommandDescription("Установить HEX-цвет подсветки через OpenRGB CLI (если доступен)")]
[CommandRisk(CommandRisk.ChangesState,true,"node.command")]
public sealed class OpenRgbSetCommand : ICommand
{
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var hex=(ctx.RawArgs??"").Trim().TrimStart('#');
        if(!System.Text.RegularExpressions.Regex.IsMatch(hex,"^[0-9a-fA-F]{6}$"))return CommandResult.Fail("invalid_color","Укажите RGB-цвет в формате #RRGGBB.");
        var exe=Environment.GetEnvironmentVariable("MYSTICEGO_OPENRGB_EXE");
        if(string.IsNullOrWhiteSpace(exe)||!File.Exists(exe))return CommandResult.Fail("openrgb_missing","Укажите доверенный путь MYSTICEGO_OPENRGB_EXE к уже установленному OpenRGB.");
        var options=new ProcessStartInfo(exe){UseShellExecute=false,RedirectStandardError=true,RedirectStandardOutput=true,CreateNoWindow=true};
        foreach(var a in new[]{"--color",hex.ToUpperInvariant()})options.ArgumentList.Add(a);
        using var process=Process.Start(options)??throw new InvalidOperationException("OpenRGB не запустился.");
        using var kill=ct.Register(()=>{try{if(!process.HasExited)process.Kill();}catch{}});
        await process.WaitForExitAsync(ct);
        if(process.ExitCode!=0)return CommandResult.Fail("openrgb_failed","OpenRGB завершился с ошибкой "+process.ExitCode);
        return CommandResult.Ok("Запрошен цвет #"+hex.ToUpperInvariant());
    }
}
