using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MysticEgo.Distributed;

public sealed record ServerSample(DateTimeOffset At, double CpuPercent, long MemoryBytes, long ManagedBytes,
    long MemoryLimitBytes, double RxBytesPerSecond, double TxBytesPerSecond, long DiskFreeBytes,
    long DiskTotalBytes, double UptimeSeconds, double? HostCpuPercent, long? HostMemoryUsedBytes, long? HostMemoryTotalBytes);

public sealed class ServerTelemetry(NodeOptions node, ILogger<ServerTelemetry> logger) : BackgroundService
{
    private readonly ConcurrentQueue<ServerSample> history = new();
    private readonly DateTimeOffset started = DateTimeOffset.UtcNow;
    public ServerSample[] History() => history.ToArray();
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var process = Process.GetCurrentProcess();
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var before = Counters();
        do
        {
            try
            {
                var now = Counters();
                var seconds = Math.Max(Stopwatch.GetElapsedTime(before.T, now.T).TotalSeconds, .01);
                var disk = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(node.DataPath))!);
                var hostCpu = now.HostTotal > before.HostTotal
                    ? 100.0 * (1.0 - (double)(now.HostIdle - before.HostIdle) / (now.HostTotal - before.HostTotal))
                    : (double?)null;
                long? hostUsed = null, hostTotal = null;
                if (OperatingSystem.IsLinux() && File.Exists("/proc/meminfo"))
                {
                    var info = File.ReadLines("/proc/meminfo").Take(4).Select(x => x.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                        .Where(x => x.Length >= 2 && x[0] is "MemTotal:" or "MemAvailable:")
                        .ToDictionary(x => x[0], x => long.Parse(x[1], CultureInfo.InvariantCulture) * 1024);
                    if (info.TryGetValue("MemTotal:", out var total) && info.TryGetValue("MemAvailable:", out var available))
                    { hostUsed = total - available; hostTotal = total; }
                }
                if (OperatingSystem.IsWindows() && Native.TryGetMemory(out var used, out var capacity))
                { hostUsed = used; hostTotal = capacity; }
                process.Refresh();
                history.Enqueue(new ServerSample(DateTimeOffset.UtcNow,
                    Math.Clamp((now.Cpu - before.Cpu).TotalSeconds * 100 / (seconds * Environment.ProcessorCount), 0, 100),
                    process.WorkingSet64, GC.GetTotalMemory(false), GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
                    Math.Max(0, now.Rx - before.Rx) / seconds, Math.Max(0, now.Tx - before.Tx) / seconds,
                    disk.AvailableFreeSpace, disk.TotalSize, (DateTimeOffset.UtcNow - started).TotalSeconds, hostCpu, hostUsed, hostTotal));
                while (history.Count > 180) history.TryDequeue(out _);
                before = now;
            }
            catch (Exception error) when (error is IOException or NetworkInformationException or UnauthorizedAccessException)
            { logger.LogWarning(error, "Resource sampling failed"); }
        } while (await timer.WaitForNextTickAsync(ct));

        (long T, TimeSpan Cpu, long Rx, long Tx, long HostTotal, long HostIdle) Counters()
        {
            process.Refresh();
            long rx = 0, tx = 0, total = 0, idle = 0;
            try
            {
                if (OperatingSystem.IsLinux())
                {
                    foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
                    {
                        var fields = line.Split(':', 2);
                        if (fields.Length != 2 || fields[0].Trim() == "lo") continue;
                        var values = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (values.Length > 8 && long.TryParse(values[0], out var received) && long.TryParse(values[8], out var sent))
                        { rx += received; tx += sent; }
                    }
                    var cpu = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1)
                        .Take(8).Select(x => long.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                    if (cpu.Length >= 5) { total = cpu.Sum(); idle = cpu[3] + cpu[4]; }
                }
                else
                {
                    if (OperatingSystem.IsWindows()) Native.GetCpu(out total, out idle);
                    foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces().Where(x =>
                                 x.NetworkInterfaceType != NetworkInterfaceType.Loopback && x.OperationalStatus == OperationalStatus.Up))
                    {
                        var sample = adapter.GetIPStatistics();
                        rx += sample.BytesReceived;
                        tx += sample.BytesSent;
                    }
                }
            }
            catch (Exception error) when (error is IOException or NetworkInformationException or UnauthorizedAccessException)
            { logger.LogDebug(error, "Host counters unavailable"); }
            return (Stopwatch.GetTimestamp(), process.TotalProcessorTime, rx, tx, total, idle);
        }
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            public uint Length, Load;
            public ulong Physical, Available, PageFile, PageAvailable, Virtual, VirtualAvailable, ExtendedAvailable;
        }

        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatus memory);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);

        public static bool TryGetMemory(out long used, out long capacity)
        {
            var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
            var ok = GlobalMemoryStatusEx(ref memory);
            capacity = (long)memory.Physical;
            used = capacity - (long)memory.Available;
            return ok;
        }

        public static void GetCpu(out long total, out long idle) =>
            total = GetSystemTimes(out idle, out var kernel, out var user) ? kernel + user : 0;
    }

    public string Prometheus()
    {
        if (history.LastOrDefault() is not { } value) return "";
        return FormattableString.Invariant($"""
            # TYPE mysticego_process_cpu_percent gauge
            mysticego_process_cpu_percent {value.CpuPercent:F3}
            # TYPE mysticego_process_memory_bytes gauge
            mysticego_process_memory_bytes {value.MemoryBytes}
            # TYPE mysticego_network_receive_bytes_per_second gauge
            mysticego_network_receive_bytes_per_second {value.RxBytesPerSecond:F3}
            # TYPE mysticego_network_transmit_bytes_per_second gauge
            mysticego_network_transmit_bytes_per_second {value.TxBytesPerSecond:F3}
            # TYPE mysticego_disk_available_bytes gauge
            mysticego_disk_available_bytes {value.DiskFreeBytes}
            # TYPE mysticego_uptime_seconds gauge
            mysticego_uptime_seconds {value.UptimeSeconds:F3}
            """) + "\n";
    }
}

