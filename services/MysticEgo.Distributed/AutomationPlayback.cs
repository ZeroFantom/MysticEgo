using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Models.Commands;

namespace MysticEgo.Distributed;

[CommandContract("automation.play", 1)]
[CommandArgument("scenario", "Сценарий из рекордера MysticEgo", Format = "json")]
[CommandDescription("Воспроизвести подтверждённые клики в активном приложении Windows")]
[CommandRisk(CommandRisk.Privileged, true, "node.command")]
public sealed class AutomationPlaybackCommand : ICommand
{
    private static readonly object Gate = new();
    private static CancellationTokenSource? active;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record Scenario(string TargetProcess, Click[] Clicks, string? CoordinateSpace = null, int StartDelayMs = 0);
    public sealed record Click(int X, int Y, int AtMs, string Button);

    internal static bool Running { get { lock (Gate) return active is not null; } }

    internal static bool Stop()
    {
        lock (Gate)
        {
            if (active is null) return false;
            active.Cancel();
            return true;
        }
    }

    public async Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows()) return CommandResult.Fail("unsupported_os", "Воспроизведение кликов доступно в Windows.");
        if (!Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0)
            return CommandResult.Fail("interactive_session_required", "Запустите Agent или Audit из AuditDesktop в пользовательской сессии Windows.");
        if (Environment.GetEnvironmentVariable("MYSTICEGO_ALLOW_AUTOMATION") != "true")
            return CommandResult.Fail("disabled", "Включите воспроизведение на целевом ПК в AuditDesktop и перезапустите роль.");
        Scenario? scenario;
        try { scenario = JsonSerializer.Deserialize<Scenario>(ctx.RawArgs ?? "", Json); }
        catch (JsonException) { return CommandResult.Fail("invalid_json", "Неверный JSON сценария."); }
        if (scenario is null || scenario.Clicks is null || scenario.Clicks.Length is < 1 or > 200 ||
            string.IsNullOrWhiteSpace(scenario.TargetProcess) ||
            !System.Text.RegularExpressions.Regex.IsMatch(scenario.TargetProcess, "^[A-Za-z0-9_.-]{2,80}$") ||
            scenario.StartDelayMs is < 0 or > 15_000 || scenario.CoordinateSpace is not (null or "screen" or "client"))
            return CommandResult.Fail("invalid_scenario", "Проверьте имя процесса, координаты и количество кликов.");
        var allowed = (Environment.GetEnvironmentVariable("MYSTICEGO_AUTOMATION_PROCESSES") ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (!allowed.Contains(scenario.TargetProcess, StringComparer.OrdinalIgnoreCase))
            return CommandResult.Fail("not_allowed", "Процесс отсутствует в списке воспроизведения AuditDesktop.");

        var last = 0;
        foreach (var click in scenario.Clicks)
        {
            if (click is null || click.AtMs < last || (long)click.AtMs + scenario.StartDelayMs > 110_000 ||
                click.Button is not ("left" or "right") || Math.Abs((long)click.X) > 64_000 || Math.Abs((long)click.Y) > 64_000 ||
                scenario.CoordinateSpace == "client" && (click.X < 0 || click.Y < 0))
                return CommandResult.Fail("invalid_click", "Клики должны идти по порядку и укладываться в 110 секунд.");
            last = click.AtMs;
        }

        using var local = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (Gate)
        {
            if (active is not null) return CommandResult.Fail("already_running", "На ПК уже выполняется сценарий.");
            active = local;
        }

        var watch = Stopwatch.StartNew();
        var count = 0;
        try
        {
            IntPtr initial = IntPtr.Zero;
            foreach (var click in scenario.Clicks)
            {
                var due = click.AtMs + scenario.StartDelayMs;
                while (watch.ElapsedMilliseconds < due)
                {
                    if (Esc()) return Stopped();
                    await Task.Delay((int)Math.Clamp(due - watch.ElapsedMilliseconds, 1, 40), local.Token);
                }
                local.Token.ThrowIfCancellationRequested();
                if (Esc()) return Stopped();
                var window = Native.GetForegroundWindow();
                if (window == IntPtr.Zero || initial != IntPtr.Zero && window != initial || !Owns(window, scenario.TargetProcess))
                    return CommandResult.Fail("focus_lost", "Окно выбранного приложения потеряло фокус.");
                initial = window;
                var point = new Native.Point(click.X, click.Y);
                if (scenario.CoordinateSpace == "client" && (!Native.GetClientRect(window, out var rect) ||
                    click.X >= rect.Right || click.Y >= rect.Bottom || !Native.ClientToScreen(window, ref point)))
                    return CommandResult.Fail("outside_window", "Клик вышел за границы целевого окна.");
                var hit = Native.WindowFromPoint(point);
                if (hit == IntPtr.Zero || Native.GetAncestor(hit, 2) != window)
                    return CommandResult.Fail("outside_window", "Клик перекрыт другим окном или вышел за границы приложения.");
                if (!Native.SetCursorPos(point.X, point.Y))
                    return CommandResult.Fail("cursor_blocked", "Windows отклонила перемещение указателя.");
                var presses = click.Button == "right"
                    ? new[] { new Native.Input(0x0008), new Native.Input(0x0010) }
                    : new[] { new Native.Input(0x0002), new Native.Input(0x0004) };
                if (Native.SendInput((uint)presses.Length, presses, Marshal.SizeOf<Native.Input>()) != 2)
                    return CommandResult.Fail("input_blocked", "Windows отклонила ввод в целевое окно.");
                count++;
                ctx.Report("click", count * 100.0 / scenario.Clicks.Length);
            }
            return CommandResult.Ok("Сценарий завершён.", new { clicks = count, process = scenario.TargetProcess });
        }
        catch (OperationCanceledException) when (local.IsCancellationRequested) { return Stopped(); }
        finally
        {
            lock (Gate) { if (ReferenceEquals(active, local)) active = null; }
        }

        CommandResult Stopped() => CommandResult.Fail("stopped", $"Сценарий остановлен. Выполнено кликов: {count}.");
    }

    private static bool Esc() => (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0;
    private static bool Owns(IntPtr window, string processName)
    {
        Native.GetWindowThreadProcessId(window, out var pid);
        try { using var process = Process.GetProcessById((int)pid); return process.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase); }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)] public record struct Point(int X, int Y);
        [StructLayout(LayoutKind.Sequential)] public struct Rect { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr handle, out uint pid);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr handle, ref Point point);
        [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr handle, out Rect rect);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr handle, uint flags);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
        [StructLayout(LayoutKind.Sequential)] public struct Mouse { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)]
        public struct Input(uint flags)
        {
            public uint Type = 0;
            public Mouse Event = new() { Flags = flags };
        }
        [DllImport("user32.dll")]
        public static extern uint SendInput(uint count, [In] Input[] inputs, int size);
    }
}

[CommandContract("automation.stop", 1)]
[CommandDescription("Остановить активный сценарий на выбранном ПК")]
[CommandRisk(CommandRisk.ChangesState, false, "node.command")]
public sealed class AutomationStopCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext ctx, CancellationToken ct = default) =>
        Task.FromResult(CommandResult.Ok(AutomationPlaybackCommand.Stop() ? "Запрошена остановка." : "Активного сценария нет."));
}
