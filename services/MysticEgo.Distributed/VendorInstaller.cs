using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace MysticEgo.Distributed;

/// <summary>Установка только из официального каталога WinGet с явным подтверждением локального пользователя.</summary>
public sealed class InstallationJobs
{
    public ConcurrentDictionary<Guid, InstallationStatus> Jobs {get;}=new();
    public SemaphoreSlim Gate {get;}=new(1,1);
}
public sealed record InstallationStatus(Guid Id,string Package,string Status,string? Message,DateTimeOffset At);


public interface IVendorInstallationService
{
    object Catalog();
    object Jobs();
    (int Status, object Result) Install(string id);
}
public sealed class VendorInstallationService(InstallationJobs jobs) : IVendorInstallationService
{
    private static readonly IReadOnlyDictionary<string,string> Packages=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
    {
        ["lmstudio"]="ElementLabs.LMStudio",["openrgb"]="OpenRGB.OpenRGB",
        ["logitech"]="Logitech.GHUB",["msicenter"]="MSI.MSICenter"
    };
    public object Catalog()=>Packages.Select(x=>new{id=x.Key,packageId=x.Value,allowed=Environment.GetEnvironmentVariable("MYSTICEGO_ALLOW_VENDOR_INSTALL")=="true"});
    public object Jobs()=>jobs.Jobs.Values.OrderByDescending(x=>x.At).Take(20).ToArray();
    public (int Status,object Result) Install(string id)
    {
        if(Environment.GetEnvironmentVariable("MYSTICEGO_ALLOW_VENDOR_INSTALL")!="true")return (400,new{error="Установка выключена (MYSTICEGO_ALLOW_VENDOR_INSTALL)."});
        if(!Packages.TryGetValue(id,out var package))return (404,new{error="unknown_package"});
        if(!jobs.Gate.Wait(0))return (409,new{error="Уже выполняется установка."});
        var job=new InstallationStatus(Guid.NewGuid(),package,"running","Установка выполняется через WinGet.",DateTimeOffset.UtcNow);
        jobs.Jobs[job.Id]=job;
        _=Task.Run(async()=>
        {
            try
            {
                var info=new ProcessStartInfo("winget.exe"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
                foreach(var arg in new[]{"install","--id",package,"--exact","--source","winget","--accept-package-agreements","--accept-source-agreements","--disable-interactivity"})info.ArgumentList.Add(arg);
                using var process=Process.Start(info)??throw new InvalidOperationException("Не удалось запустить WinGet.");
                using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(15));
                var stdout=process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr=process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var output=await stdout;var error=await stderr;
                var message=process.ExitCode==0?output:error;
                jobs.Jobs[job.Id]=job with {Status=process.ExitCode==0?"success":"error",Message=message[..Math.Min(1000,message.Length)]};
            }
            catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or InvalidOperationException or OperationCanceledException)
            {jobs.Jobs[job.Id]=job with {Status="error",Message=ex.Message};}
            finally{jobs.Gate.Release();}
        });
        return (202,job);
    }
}
