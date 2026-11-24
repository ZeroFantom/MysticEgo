using System.Diagnostics;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Clients
{
    /// <summary>
    /// Периодически шлёт heartbeat на сервер. Работает в фоне.
    /// Останавливается, когда клиент не подключён.
    /// </summary>
    public sealed class HeartbeatService : IDisposable
    {
        private readonly AgentHubClientOptions _options;
        private readonly Func<HubConnection?> _connectionAccessor;
        private readonly Func<int> _runningCommandsAccessor;
        private readonly ILogger<HeartbeatService> _log;

        private readonly CancellationTokenSource _cts = new();
        private Task? _loop;

        public HeartbeatService(
            AgentHubClientOptions options,
            Func<HubConnection?> connectionAccessor,
            Func<int> runningCommandsAccessor,
            ILogger<HeartbeatService> log)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _connectionAccessor = connectionAccessor
                                  ?? throw new ArgumentNullException(nameof(connectionAccessor));
            _runningCommandsAccessor = runningCommandsAccessor
                                       ?? throw new ArgumentNullException(nameof(runningCommandsAccessor));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start()
        {
            if (_loop is not null) return;
            _loop = Task.Run(() => LoopAsync(_cts.Token));
        }

        public async Task StopAsync()
        {
            try { _cts.Cancel(); } catch { }

            if (_loop is not null)
            {
                try { await _loop.ConfigureAwait(false); } catch { }
                _loop = null;
            }
        }

        public void Dispose()
        {
            try { _cts.Cancel(); } catch { }
            try { _cts.Dispose(); } catch { }
        }

        private async Task LoopAsync(CancellationToken ct)
        {
            var uptime = Stopwatch.StartNew();

            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(_options.HeartbeatInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }

                var conn = _connectionAccessor();
                if (conn is null || conn.State != HubConnectionState.Connected) continue;

                try
                {
                    var report = new HeartbeatReport
                    {
                        ClientId = _options.ClientId,
                        UptimeSeconds = (long)uptime.Elapsed.TotalSeconds,
                        RunningCommands = _runningCommandsAccessor(),
                    };

                    await conn
                        .InvokeAsync(nameof(IAgentHub.Heartbeat), report, ct)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Heartbeat failed");
                }
            }
        }
    }
}