using System.Diagnostics;
using System.Runtime.InteropServices;

internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr window, string message, string title, uint type);

    private static int Main()
    {
        var root = AppContext.BaseDirectory;
        try
        {
            var electron = Path.Combine(root, "runtime", "electron", "electron.exe");
            #if AUDIT_DESKTOP
            var app = Path.Combine(root, "desktop", "station");
#else
            var app = Path.Combine(root, "desktop", "control");
#endif
            if (!File.Exists(electron) || !File.Exists(Path.Combine(app, "main.mjs")))
                throw new FileNotFoundException("Распакуйте архив решения полностью, сохранив папки desktop и runtime рядом с приложением.");
            var start = new ProcessStartInfo(electron) { WorkingDirectory = root, UseShellExecute = false };
            start.ArgumentList.Add(app);
            start.Environment.Remove("ELECTRON_RUN_AS_NODE");
            using var child = Process.Start(start);
            return 0;
        }
        catch (Exception error)
        {
            MessageBoxW(IntPtr.Zero, error.Message, "MysticEgo", 0x10);
            return 1;
        }
    }
}
