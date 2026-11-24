using System.Diagnostics;
using System.Security.Cryptography;

internal static class LabPackage
{
    internal static readonly Dictionary<string,(string Project,int Port)> Roles = new()
    {
        ["auth"]=("Api.Auth",5400), ["orchestra"]=("Api.Orchestra",5500),
        ["global"]=("Api.Global",5100), ["audit"]=("Api.AuditOrchestra",5350),
        ["local"]=("Api.Local",5200), ["agent"]=("Api.Agent",5300)
    };

    internal static async Task RunTool(string exe, string[] args, string cwd, CancellationToken ct = default, IReadOnlyDictionary<string,string>? environment = null)
    {
        var start = new ProcessStartInfo(exe){UseShellExecute=false,WorkingDirectory=cwd,CreateNoWindow=Environment.GetEnvironmentVariable("MYSTICEGO_GUI_SESSION")=="1"};
        if(environment != null) foreach(var (name,value) in environment) start.Environment[name]=value;
        foreach(var arg in args) start.ArgumentList.Add(arg);
        using var child=Process.Start(start) ?? throw new IOException("Failed to start " + exe);
        try {await child.WaitForExitAsync(ct);}
        catch(OperationCanceledException){if(!child.HasExited)child.Kill(true);throw;}
        if(child.ExitCode!=0)throw new IOException($"{exe} завершился с кодом {child.ExitCode}.");
    }

    internal static async Task Build(string root, bool reuseWeb)
    {
        var output=Path.Combine(root,"runtime","app");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var stage=Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(output)!,".stage-"+Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            string npm=OperatingSystem.IsWindows()?"cmd.exe":"npm";
            string[] Npm(string[] args)=>OperatingSystem.IsWindows()?["/d","/c","npm",..args]:args;
            var front=Path.Combine(root,"frontend");
            if(!reuseWeb)
            {
                await RunTool(npm,Npm(["ci","--no-audit","--no-fund"]),front);
                await RunTool(npm,Npm(["run","build"]),front);
                var desktop=Path.Combine(root,"desktop");
                await RunTool(npm,Npm(["ci","--no-audit","--no-fund"]),desktop);
                await RunTool(npm,Npm(["run","build"]),desktop);
            }
            var merged=Path.Combine(stage,"merged");
            foreach(var role in Roles.Keys.Concat(new[]{"installer","geometry","recorder"}))
            {
                var name=role switch{"installer"=>"MysticEgo.Installer","geometry"=>"MysticEgo.DesktopGeometry","recorder"=>"MysticEgo.AutomationRecorder",_=>Roles[role].Project};
                var project=role=="geometry"?Path.Combine(root,"desktop","native",name+".csproj"):Path.Combine(root,role=="auth"?"core":role is "installer" or "recorder"?"tools":"services",name,name+".csproj");
                var pub=Path.Combine(stage,role);
                await RunTool("dotnet",["publish",project,"-c","Release","--self-contained","false","-p:UseAppHost=false","-p:DebugType=None","-m:1","-o",pub],root);
                foreach(var file in Directory.EnumerateFiles(pub,"*",SearchOption.AllDirectories))
                {
                    var relative=Path.GetRelativePath(pub,file).Replace('\\','/');
                    if(relative.StartsWith("runtimes/") && !new[]{"win-x64/","linux-x64/","win/","unix/"}.Any(x=>relative.StartsWith("runtimes/"+x)))continue;
                    if(relative.StartsWith("wwwroot/")||relative.Contains("staticwebassets.")||relative.EndsWith(".pdb")||relative=="appsettings.Development.json"||relative=="web.config")continue;
                    var dst=relative=="appsettings.json"?Path.Combine(merged,"settings",role+".json"):Path.Combine(merged,"bin",relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    if(File.Exists(dst))
                    {
                        if(!SHA256.HashData(File.ReadAllBytes(file)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(dst))))
                            throw new IOException("Conflicting published file: "+relative);
                    }
                    else File.Copy(file,dst);
                }
                if(role is "auth" or "installer" or "geometry" or "recorder")continue;
                var dist=Path.Combine(front,"apps",role,"dist");
                foreach(var file in Directory.EnumerateFiles(dist,"*",SearchOption.AllDirectories))
                {
                    var target=Path.Combine(merged,"web",role,Path.GetRelativePath(dist,file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);File.Copy(file,target);
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var previous=output+".previous";
            if(Directory.Exists(previous))throw new IOException("Previous packaging backup exists: "+previous);
            if(Directory.Exists(output))Directory.Move(output,previous);
            try{Directory.Move(merged,output);}
            catch{if(Directory.Exists(previous))Directory.Move(previous,output);throw;}
            if(Directory.Exists(previous))Directory.Delete(previous,true);
        }
        finally{Directory.Delete(stage,true);}
    }
}
