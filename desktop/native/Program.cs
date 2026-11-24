using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

var result=new List<WindowInfo>();
if(OperatingSystem.IsWindows())
{
    Native.EnumWindows((hWnd,_)=>
    {
        if(!Native.IsWindowVisible(hWnd)||Native.IsIconic(hWnd))return true;
        var len=Native.GetWindowTextLength(hWnd);if(len<2||len>300)return true;
        var title=new StringBuilder(len+1);Native.GetWindowText(hWnd,title,title.Capacity);
        if(!Native.GetWindowRect(hWnd,out var bounds))return true;
        var w=bounds.Right-bounds.Left;var h=bounds.Bottom-bounds.Top;
        if(w>=100&&h>=100)result.Add(new WindowInfo(title.ToString(),bounds.Left,bounds.Top,w,h));
        return true;
    },IntPtr.Zero);
}
else if(OperatingSystem.IsLinux())
{
    // Window positioning requires an X11 compositor + installed wmctrl. Wayland denies arbitrary window enumeration.
    try {
        var process=Process.Start(new ProcessStartInfo("wmctrl","-lG") {RedirectStandardOutput=true,UseShellExecute=false,CreateNoWindow=true});
        if(process!=null)
        {
            var output=await process.StandardOutput.ReadToEndAsync();await process.WaitForExitAsync();
            foreach(var line in output.Split('\n',StringSplitOptions.RemoveEmptyEntries))
            {
                var tokens=line.Split((char[]?)null,8,StringSplitOptions.RemoveEmptyEntries);
                if(tokens.Length<8)continue;
                if(int.TryParse(tokens[2],out var x)&&int.TryParse(tokens[3],out var y)&&int.TryParse(tokens[4],out var w)&&int.TryParse(tokens[5],out var h)&&w>=100&&h>=100)
                    result.Add(new WindowInfo(tokens[7],x,y,w,h));
            }
        }
    }catch(System.ComponentModel.Win32Exception){}
}
Console.WriteLine(JsonSerializer.Serialize(result));
public sealed record WindowInfo(string Title,int X,int Y,int Width,int Height);
internal static class Native
{
    internal delegate bool EnumWindowsCallback(IntPtr hwnd,IntPtr lparam);
    [DllImport("user32.dll")]internal static extern bool EnumWindows(EnumWindowsCallback callback,IntPtr extra);
    [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int GetWindowText(IntPtr hwnd,StringBuilder value,int capacity);
    [DllImport("user32.dll")]internal static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
    [StructLayout(LayoutKind.Sequential)]internal struct Rect{public int Left,Top,Right,Bottom;}
}
