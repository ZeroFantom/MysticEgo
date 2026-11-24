using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

// Интерактивный рекордер мыши для тестовой автоматизации приложений Windows.
// Не записывает клавиатуру, пароли, текст, содержимое окон или файлы пользователя.
// Запускать только на своём ПК, вручную, в явном режиме записи.
if(!OperatingSystem.IsWindows() || args.Length!=3 || args[0]!="record")
{
    Console.WriteLine("Откройте запись сценария из AuditDesktop в Windows.");
    return;
}
var bearer = Environment.GetEnvironmentVariable("MYSTICEGO_RECORDER_SESSION");
var server = Environment.GetEnvironmentVariable("MYSTICEGO_RECORDER_SERVER");
if (string.IsNullOrWhiteSpace(bearer) || !Uri.TryCreate(server, UriKind.Absolute, out var auth))
{
    Console.Error.WriteLine("Откройте AuditDesktop и войдите перед записью.");
    Environment.ExitCode = 3;
    return;
}
try
{
    using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
    using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(auth, "/api/v1/collective/me"));
    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
    using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Требуется действующая учётная запись.");
}
catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or OperationCanceledException)
{
    Console.Error.WriteLine("Не удалось подтвердить вход в AuditDesktop.");
    Environment.ExitCode = 3;
    return;
}
var processName=args[1];
if(!System.Text.RegularExpressions.Regex.IsMatch(processName,"^[A-Za-z0-9_.-]{2,80}$"))
{
    Console.Error.WriteLine("Недопустимое имя процесса.");return;
}
var events=new List<Click>();
var timer=Stopwatch.StartNew();
Console.WriteLine($"Запись мыши ТОЛЬКО в активном приложении «{processName}». Нажмите Enter для остановки.");
Console.WriteLine("Будут записаны координаты кликов, но не вводимый текст. Максимум 200 событий.");
var threadId=Native.GetCurrentThreadId();
Native.LowLevelMouseProc callback=(code,wParam,lParam)=>
{
    if(code>=0 && (wParam==new IntPtr(0x201) || wParam==new IntPtr(0x204)) && events.Count<200)
    {
        try
        {
            var window = Native.GetForegroundWindow();
            Native.GetWindowThreadProcessId(window, out var pid);
            using var p=Process.GetProcessById((int)pid);
            if(p.ProcessName.Equals(processName,StringComparison.OrdinalIgnoreCase))
            {
                var info=Marshal.PtrToStructure<Native.MouseStruct>(lParam);
                var point = info.pt;
                if (Native.GetAncestor(Native.WindowFromPoint(point), 2) == window &&
                    Native.ScreenToClient(window, ref point) && point.x >= 0 && point.y >= 0)
                {
                    if (events.Count == 0) timer.Restart();
                    if (timer.ElapsedMilliseconds <= 100_000)
                        events.Add(new Click(point.x, point.y, (int)timer.ElapsedMilliseconds, wParam == new IntPtr(0x204) ? "right" : "left"));
                }
            }
        }catch(ArgumentException){}catch(InvalidOperationException){}
    }
    return Native.CallNextHookEx(IntPtr.Zero,code,wParam,lParam);
};
var hook=Native.SetWindowsHookEx(14,callback,IntPtr.Zero,0);
if(hook==IntPtr.Zero){Console.Error.WriteLine("Не удалось включить запись кликов.");Environment.ExitCode=2;return;}
try
{
    var stop=new Thread(()=>{Console.ReadLine();Native.PostThreadMessage(threadId,0x12,IntPtr.Zero,IntPtr.Zero);}){IsBackground=true};stop.Start();
    while(Native.GetMessage(out _,IntPtr.Zero,0,0)>0){}
}
finally{Native.UnhookWindowsHookEx(hook);GC.KeepAlive(callback);}
if (events.Count == 0)
{
    Console.Error.WriteLine("Не записано ни одного клика в выбранном окне.");
    Environment.ExitCode = 2;
    return;
}
var scenario = new { targetProcess = processName, coordinateSpace = "client", clicks = events };
await File.WriteAllTextAsync(Path.GetFullPath(args[2]),JsonSerializer.Serialize(scenario,new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
Console.WriteLine($"Сценарий записан: {events.Count} кликов. Файл: {Path.GetFullPath(args[2])}");

public sealed record Click(int X,int Y,int AtMs,string Button);
internal static class Native
{
    public delegate IntPtr LowLevelMouseProc(int nCode,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]public struct Point{public int x,y;}
    [StructLayout(LayoutKind.Sequential)]public struct MouseStruct{public Point pt;public uint mouseData,flags,time;public IntPtr dwExtraInfo;}
    [DllImport("user32.dll")]public static extern IntPtr SetWindowsHookEx(int idHook,LowLevelMouseProc proc,IntPtr hMod,uint threadId);
    [DllImport("user32.dll")]public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")]public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wParam,IntPtr lParam);
    [DllImport("user32.dll")]public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]public static extern bool ScreenToClient(IntPtr window, ref Point point);
    [DllImport("user32.dll")]public static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")]public static extern IntPtr GetAncestor(IntPtr handle, uint flag);
    [DllImport("user32.dll")]public static extern uint GetWindowThreadProcessId(IntPtr handle,out uint processId);
    [DllImport("kernel32.dll")]public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]public static extern bool PostThreadMessage(uint thread,uint msg,IntPtr wParam,IntPtr lParam);
    [StructLayout(LayoutKind.Sequential)]public struct Msg{public IntPtr hwnd;public uint message;public IntPtr wParam;public IntPtr lParam;public uint time;public Point pt;public uint lPrivate;}
    [DllImport("user32.dll")]public static extern int GetMessage(out Msg message,IntPtr hwnd,uint min,uint max);
}
