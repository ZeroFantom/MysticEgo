using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

try
{
    if (!OperatingSystem.IsWindows() || args.Length < 2) throw new InvalidOperationException();
    var op = args[0];
    var process = args[1];
    if (op == "pointer-get" || op == "pointer-set")
    {
        if (op == "pointer-set")
        {
            var speed = int.Parse(args[2], CultureInfo.InvariantCulture);
            if (speed is < 1 or > 20 || !Native.SetSpeed(0x0071, 0, (IntPtr)speed, 3))
                throw new InvalidOperationException();
        }
        if (!Native.GetSpeed(0x0070, 0, out var current, 0)) throw new InvalidOperationException();
        Console.WriteLine(JsonSerializer.Serialize(new { speed = current }));
        return;
    }
    var hwnd = Native.GetForegroundWindow();
    if (hwnd == IntPtr.Zero) throw new InvalidOperationException();
    Native.GetWindowThreadProcessId(hwnd, out var pid);
    using var target = Process.GetProcessById((int)pid);
    if (!string.Equals(target.ProcessName, process, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Select the configured window first");
    if (op != "attach" && (args.Length < 4 || hwnd.ToInt64().ToString(CultureInfo.InvariantCulture) != args[2] ||
        pid.ToString(CultureInfo.InvariantCulture) != args[3]))
        throw new InvalidOperationException("Foreground window changed");
    if (!Native.GetClientRect(hwnd, out var rect)) throw new InvalidOperationException();
    var origin = new Native.Point();
    if (!Native.ClientToScreen(hwnd, ref origin)) throw new InvalidOperationException();
    var width = rect.Right; var height = rect.Bottom;
    if (width < 1 || height < 1 || (long)width * height > 12_000_000) throw new InvalidOperationException();
    void CheckFocus()
    {
        if (Native.GetForegroundWindow() != hwnd) throw new InvalidOperationException("Focus changed");
    }
    if (op == "attach")
    {
        Console.WriteLine(JsonSerializer.Serialize(new { hwnd = hwnd.ToInt64().ToString(CultureInfo.InvariantCulture), pid = pid.ToString(CultureInfo.InvariantCulture), width, height }));
    }
    else if (op == "snapshot")
    {
        CheckFocus();
        using var bitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(bitmap)) graphics.CopyFromScreen(origin.X, origin.Y, 0, 0, bitmap.Size);
        CheckFocus();
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        Console.WriteLine(JsonSerializer.Serialize(new { image = Convert.ToBase64String(stream.ToArray()), width, height }));
    }
    else if (op == "click")
    {
        var x = int.Parse(args[4], CultureInfo.InvariantCulture);
        var y = int.Parse(args[5], CultureInfo.InvariantCulture);
        if (x < 0 || y < 0 || x >= width || y >= height) throw new InvalidOperationException();
        var point = new Native.Point { X = origin.X + x, Y = origin.Y + y };
        if (Native.GetAncestor(Native.WindowFromPoint(point), 2) != hwnd) throw new InvalidOperationException("Target is obscured");
        CheckFocus();
        if (!Native.SetCursorPos(point.X, point.Y)) throw new InvalidOperationException();
        CheckFocus();
        Native.Click(args[6] == "right");
        Console.WriteLine("{\"sent\":true}");
    }
    else if (op == "key")
    {
        var key = args[4];
        if (!Native.Keys.TryGetValue(key, out var vk)) throw new InvalidOperationException();
        CheckFocus();
        Native.Key(vk);
        Console.WriteLine("{\"sent\":true}");
    }
    else throw new InvalidOperationException();
}
catch (Exception)
{
    Console.Error.WriteLine("Native operation failed; verify foreground target and permissions.");
    Environment.ExitCode = 1;
}

internal static class Native
{
    internal static readonly Dictionary<string, ushort> Keys = new(StringComparer.Ordinal)
    {
        ["Enter"] = 0x0D, ["Escape"] = 0x1B, ["Tab"] = 0x09, ["Space"] = 0x20,
        ["Up"] = 0x26, ["Down"] = 0x28, ["Left"] = 0x25, ["Right"] = 0x27,
        ["W"] = 0x57, ["A"] = 0x41, ["S"] = 0x53, ["D"] = 0x44
    };
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Mouse { public int dx,dy; public uint data,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] private struct Keyboard { public ushort vk,scan; public uint flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion
    { [FieldOffset(0)] public Mouse mouse; [FieldOffset(0)] public Keyboard keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint type; public InputUnion value; }
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] internal static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd,uint flags);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, Input[] inputs,int size);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW")] internal static extern bool GetSpeed(uint action,uint param,out int value,uint flags);
    [DllImport("user32.dll", EntryPoint="SystemParametersInfoW")] internal static extern bool SetSpeed(uint action,uint param,IntPtr value,uint flags);
    private static void Send(Input down,Input up)
    {
        var size=Marshal.SizeOf<Input>();
        var count=SendInput(2,[down,up],size);
        if(count==1) SendInput(1,[up],size);
        if(count!=2) throw new InvalidOperationException();
    }
    internal static void Click(bool right)
    {
        Send(new Input { value = new InputUnion { mouse = new Mouse { flags=right?8u:2u } } },
             new Input { value = new InputUnion { mouse = new Mouse { flags=right?16u:4u } } });
    }
    internal static void Key(ushort vk)
    {
        Send(new Input { type=1,value=new InputUnion { keyboard=new Keyboard { vk=vk } } },
             new Input { type=1,value=new InputUnion { keyboard=new Keyboard { vk=vk,flags=2 } } });
    }
}
