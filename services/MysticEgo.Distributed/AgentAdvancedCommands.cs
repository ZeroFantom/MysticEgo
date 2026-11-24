using Models.Commands;
using System.Diagnostics;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Net.Http;

namespace MysticEgo.Distributed;

/// <summary>Расширенная диагностика машины без чтения личных файлов.</summary>
[CommandContract("system.details", 1)]
[CommandDescription("Подробная диагностика ОС, CPU, памяти и накопителей")]
public sealed class SystemDetailsCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct=default)
    {
        var memory=GC.GetGCMemoryInfo();
        var data=new {
            host=Environment.MachineName, user=Environment.UserName,
            os=RuntimeInformation.OSDescription, architecture=RuntimeInformation.OSArchitecture.ToString(),
            cpuArchitecture=RuntimeInformation.ProcessArchitecture.ToString(),
            cpuThreads=Environment.ProcessorCount, ramAvailableForGcBytes=memory.TotalAvailableMemoryBytes,
            runtime=RuntimeInformation.FrameworkDescription, uptimeMilliseconds=Environment.TickCount64,
            disks=DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>new {d.Name,d.DriveType,d.DriveFormat,d.TotalSize,d.AvailableFreeSpace}).ToArray(),
            at=DateTimeOffset.UtcNow
        };
        return Task.FromResult(CommandResult.Ok(structured:data));
    }
}

/// <summary>Список процессов с ограниченным количеством записей; запрещено читать командные строки и переменные окружения процессов.</summary>
[CommandContract("process.details",1)]
[CommandDescription("Список процессов, использование памяти, время запуска")]
public sealed class ProcessDetailsCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var list=new List<object>();
        foreach(var proc in Process.GetProcesses())
        {
            using(proc)
            {
                try {
                    DateTime? started=null;
                    try {started=proc.StartTime.ToUniversalTime();}catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
                    list.Add(new {pid=proc.Id,name=proc.ProcessName,workingSetBytes=proc.WorkingSet64,threads=proc.Threads.Count,startedUtc=started});
                }catch(System.ComponentModel.Win32Exception){}catch(InvalidOperationException){}
            }
            if(list.Count>=1000)break;
        }
        return Task.FromResult(CommandResult.Ok(structured:list));
    }
}

/// <summary>Статус внешних пакетов. Никакого скачивания стороннего ПО с непроверенного адреса.</summary>
[CommandContract("integrations.status",1)]
[CommandDescription("Проверка доступности LM Studio, NVIDIA, Logitech G HUB, MSI Center и OpenRGB")]
public sealed class IntegrationsStatusCommand : ICommand
{
    public async Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        static bool Running(string name)
        {
            var ps=Process.GetProcessesByName(name);
            try {return ps.Length>0;}finally {foreach(var p in ps)p.Dispose();}
        }
        bool lm=false;
        try {using var client=new HttpClient{Timeout=TimeSpan.FromSeconds(2)};
            using var response=await client.GetAsync("http://127.0.0.1:1234/v1/models",ct);
            lm=response.IsSuccessStatusCode;
        }catch(HttpRequestException){}catch(TaskCanceledException){}
        return CommandResult.Ok(structured:new {
            openRgb=Running("OpenRGB"),logitechGHub=Running("lghub"),
            msiCenter=Running("MSI.CentralServer")||Running("MSI_Center"),
            nvidia=Running("nvcontainer")||Running("NVIDIA App"),
            lmStudioApi=lm,note="Статус процессов не является полной проверкой установки ПО"
        });
    }
}

/// <summary>Запуск только зарегистрированного в конфигурации ярлыка. Работает в интерактивной Windows-сессии, не в Session 0 службы.</summary>
[CommandContract("app.launch",1)]
[CommandArgument("alias", "Псевдоним из локального списка приложений", Example = "dota2")]
[CommandDescription("Запустить приложение по заранее разрешённому ярлыку")]
[CommandRisk(CommandRisk.Privileged,true,"node.command")]
public sealed class LaunchAllowlistedAppCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        var alias=(ctx.RawArgs??"").Trim();
        if(!System.Text.RegularExpressions.Regex.IsMatch(alias,"^[a-zA-Z0-9_.-]{1,40}$"))
            return Task.FromResult(CommandResult.Fail("invalid_alias","Укажите зарегистрированный псевдоним, например dota2."));
        var raw=Environment.GetEnvironmentVariable("MYSTICEGO_LAUNCH_ALLOWLIST_JSON")??"{}";
        try
        {
            var apps=JsonSerializer.Deserialize<Dictionary<string,string>>(raw)??new();
            if(!apps.TryGetValue(alias,out var path)||!Path.IsPathFullyQualified(path)||!File.Exists(path))
                return Task.FromResult(CommandResult.Fail("not_allowed","Приложение отсутствует в локальном разрешённом списке."));
            ct.ThrowIfCancellationRequested();
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});
            return Task.FromResult(CommandResult.Ok($"Запуск приложения «{alias}» запрошен."));
        }
        catch(Exception ex) when(ex is JsonException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {return Task.FromResult(CommandResult.Fail("launch_failed",ex.Message));}
    }
}

/// <summary>Выключение ПК по умолчанию запрещено, требует отдельного конфигурационного согласия владельца.</summary>
[CommandContract("system.power",1)]
[CommandArgument("action", "Действие с питанием", Values = new[] { "restart", "shutdown", "cancel" }, Default = "restart")]
[CommandDescription("Перезагрузка или выключение Windows с задержкой и возможностью отмены")]
[CommandRisk(CommandRisk.Privileged,true,"node.command")]
public sealed class SystemPowerCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx,CancellationToken ct=default)
    {
        if(!OperatingSystem.IsWindows())return Task.FromResult(CommandResult.Fail("unsupported_os","Команда поддерживается на Windows."));
        if(Environment.GetEnvironmentVariable("MYSTICEGO_ALLOW_POWER")!="true")
            return Task.FromResult(CommandResult.Fail("disabled","Владелец машины не разрешил удалённое управление питанием."));
        var action=(ctx.RawArgs??"").Trim().ToLowerInvariant();
        if(action is not ("restart" or "shutdown" or "cancel"))
            return Task.FromResult(CommandResult.Fail("invalid_action","Допустимые аргументы: restart, shutdown, cancel."));
        var flags=action switch{"restart"=>new[]{"/r","/t","60","/c","MysticEgo: scheduled restart"},"shutdown"=>new[]{"/s","/t","60","/c","MysticEgo: scheduled shutdown"},_=>new[]{"/a"}};
        try
        {
            ct.ThrowIfCancellationRequested();
            var info=new ProcessStartInfo("shutdown.exe"){UseShellExecute=false,CreateNoWindow=true};
            foreach(var f in flags)info.ArgumentList.Add(f);
            using var p=Process.Start(info);
            return Task.FromResult(p is null?CommandResult.Fail("start_failed","Не удалось запустить shutdown.exe"):CommandResult.Ok("Запрос передан Windows. До выключения — 60 секунд; отмена: shutdown /a."));
        }catch(System.ComponentModel.Win32Exception e){return Task.FromResult(CommandResult.Fail("power_failed",e.Message));}
    }
}
