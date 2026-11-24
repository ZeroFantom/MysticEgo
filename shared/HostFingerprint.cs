using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace MysticEgo.Deployment;

public static class HostFingerprint
{
    public static string Read()
    {
        var source = Environment.MachineName;
        try
        {
            if (OperatingSystem.IsWindows())
                source = (string?)Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography", "MachineGuid", null) ?? source;
            else if (OperatingSystem.IsLinux())
            {
                const string mountedHostId = "/run/mysticego-host-machine-id";
                if (File.Exists("/.dockerenv") && File.Exists(mountedHostId)) source = File.ReadAllText(mountedHostId);
                else if (File.Exists("/etc/machine-id")) source = File.ReadAllText("/etc/machine-id");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("MysticEgo host|" + source.Trim().ToLowerInvariant()))).ToLowerInvariant();
    }
}
