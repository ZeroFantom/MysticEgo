using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Commands.Scanner;

namespace Models.Commands.SignalR.Clients
{
    /// <summary>
    /// Базовый клиент для подключения к AgentHub. Наследуй и расширяй.
    ///
    /// Гарантии:
    ///   • Ни один метод не бросает исключений наружу.
    ///   • Auto-reconnect с бэкоффом из коробки.
    ///   • Auto-sync capabilities при Connected и Reconnected.
    ///   • Auto-heartbeat (если включён).
    ///   • Обработка входящих команд через ICommandInvoker.
    ///   • Прогресс выполнения через IAgentHub.ReportProgress.
    /// </summary>
    public abstract class AgentHubClientBase : IAsyncDisposable
    {
        private readonly ILogger _logger;
        private readonly ICapabilityScanner _scanner;
        private readonly ICommandInvoker _invoker;
        private readonly HeartbeatService _heartbeat;
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly object _stateLock = new();

        private HubConnection? _connection;
        private Task? _supervisor;
        private CancellationTokenSource? _cts;

        private int _runningCommands;
        private int _disposed;
        private bool _disposedFlag;

        protected AgentHubClientOptions Options { get; }

        protected HubConnection? Connection
        {
            get { lock (_stateLock) return _connection; }
        }

        public bool IsConnected
            => Connection?.State == HubConnectionState.Connected;

        protected int RunningCommands => Volatile.Read(ref _runningCommands);

        protected AgentHubClientBase(
            AgentHubClientOptions options,
            ICapabilityScanner scanner,
            ICommandInvoker invoker,
            ILogger logger)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
            _invoker = invoker ?? throw new ArgumentNullException(nameof(invoker));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            if (string.IsNullOrWhiteSpace(options.HubUrl))
                throw new InvalidOperationException("Не указан адрес SignalR в AgentHubClientOptions.HubUrl.");

            if (options.ClientId == Guid.Empty)
                throw new InvalidOperationException("Не задан идентификатор AgentHubClientOptions.ClientId.");

            _heartbeat = new HeartbeatService(
                options,
                () => Connection,
                () => Volatile.Read(ref _runningCommands),
                _logger is ILogger<HeartbeatService> hb
                    ? hb
                    : NullLogger<HeartbeatService>.Instance);
        }

        // ── Lifecycle ────────────────────────────────────────

        public virtual async Task StartAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_supervisor is not null)
                {
                    _logger.LogWarning("AgentHubClient уже запущен.");
                    return;
                }

                _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

                await BuildConnectionAsync(_cts.Token).ConfigureAwait(false);

                if (Options.AutoHeartbeat)
                    _heartbeat.Start();

                _supervisor = Task.Run(() => SuperviseAsync(_cts.Token));

                _logger.LogInformation(
                    "AgentHubClient запущен: {Url}, clientId={ClientId}",
                    Options.HubUrl, Options.ClientId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка запуска AgentHubClient");
                throw;
            }
            finally { _gate.Release(); }
        }

        public virtual async Task StopAsync(CancellationToken ct = default)
        {
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                try { _cts?.Cancel(); } catch { }
                try { await _heartbeat.StopAsync().ConfigureAwait(false); } catch { }

                if (_supervisor is not null)
                {
                    try { await _supervisor.WaitAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false); }
                    catch { }
                    _supervisor = null;
                }

                await CloseConnectionAsync().ConfigureAwait(false);

                _cts?.Dispose();
                _cts = null;

                _logger.LogInformation("AgentHubClient остановлен.");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка остановки AgentHubClient");
            }
            finally { _gate.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _disposedFlag = true;

            try { await StopAsync().ConfigureAwait(false); } catch { }

            _heartbeat.Dispose();
            _gate.Dispose();

            GC.SuppressFinalize(this);
        }

        // ── Public API ───────────────────────────────────────

        public virtual async Task<bool> SyncCapabilitiesAsync(CancellationToken ct = default)
        {
            var conn = Connection;

            if (conn is null || conn.State != HubConnectionState.Connected)
            {
                _logger.LogDebug("SyncCapabilities: нет подключения.");
                return false;
            }

            try
            {
                var assemblies = ResolveAssemblies();
                var caps = _scanner.Scan(assemblies);

                var request = new CapabilitySyncRequest
                {
                    Capabilities = caps,
                    ClientVersion = ResolveClientVersion(),
                    MachineGuid = Options.ClientId.ToString("D"),
                };

                var response = await conn
                    .InvokeAsync<CapabilitySyncResponse>(
                        nameof(IAgentHub.SyncCapabilities), request, ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Capabilities синхронизированы: отправлено {Sent}, принято {Accepted}",
                    caps.Count, response?.Accepted ?? 0);

                await OnCapabilitiesSyncedAsync(caps, response, ct).ConfigureAwait(false);
                return true;
            }
            catch (OperationCanceledException) { return false; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SyncCapabilities failed");
                return false;
            }
        }

        public virtual async Task<bool> SendHeartbeatAsync(CancellationToken ct = default)
        {
            var conn = Connection;
            if (conn is null || conn.State != HubConnectionState.Connected)
                return false;

            try
            {
                await conn.InvokeAsync(
                    nameof(IAgentHub.Heartbeat),
                    new HeartbeatReport
                    {
                        ClientId = Options.ClientId,
                        RunningCommands = Volatile.Read(ref _runningCommands),
                    },
                    ct).ConfigureAwait(false);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "SendHeartbeat failed");
                return false;
            }
        }

        // ── Supervisor ───────────────────────────────────────

        private async Task SuperviseAsync(CancellationToken ct)
        {
            var attempt = 0;

            while (!ct.IsCancellationRequested)
            {
                var conn = Connection;

                if (conn is null)
                {
                    try { await BuildConnectionAsync(ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Не удалось собрать HubConnection");
                    }
                }
                else if (conn.State == HubConnectionState.Disconnected)
                {
                    attempt++;
                    var delay = ComputeReconnectDelay(attempt);

                    _logger.LogWarning(
                        "Переподключение #{Attempt} через {Delay} c...",
                        attempt, delay.TotalSeconds);

                    try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { return; }

                    try
                    {
                        await conn.StartAsync(ct).ConfigureAwait(false);
                        if (Options.AutoSyncCapabilities) await SyncCapabilitiesAsync(ct).ConfigureAwait(false);
                        attempt = 0;
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "StartAsync failed (attempt #{Attempt})", attempt);
                    }
                }
                else attempt = 0;

                try { await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
            }
        }

        private static TimeSpan ComputeReconnectDelay(int attempt)
            => TimeSpan.FromSeconds(Math.Pow(2, Math.Min(attempt, 6)));

        // ── Connection building ──────────────────────────────

        private async Task BuildConnectionAsync(CancellationToken ct)
        {
            await CloseConnectionAsync().ConfigureAwait(false);

            var conn = new HubConnectionBuilder()
                .WithUrl(Options.HubUrl, opts =>
                {
                    if (Options.AccessTokenProvider is not null)
                        opts.AccessTokenProvider = async () => await Options.AccessTokenProvider(CancellationToken.None).ConfigureAwait(false);
                    else if (!string.IsNullOrWhiteSpace(Options.ApiToken))
                        opts.AccessTokenProvider = () => Task.FromResult<string?>(Options.ApiToken);

                    foreach (var (k, v) in Options.AdditionalHeaders)
                        opts.Headers[k] = v;
                })
                .AddJsonProtocol(p => p.PayloadSerializerOptions = ContractsJsonOptions.Create())
                .WithAutomaticReconnect(new[]
                {
                    TimeSpan.Zero,
                    TimeSpan.FromSeconds(2),
                    TimeSpan.FromSeconds(5),
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromSeconds(30),
                })
                .Build();

            AttachHandlers(conn);

            lock (_stateLock) _connection = conn;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Options.ConnectTimeout);

            try
            {
                await conn.StartAsync(cts.Token).ConfigureAwait(false);
                if (Options.AutoSyncCapabilities) await SyncCapabilitiesAsync(cts.Token).ConfigureAwait(false);
                _logger.LogInformation("Подключено: {ConnectionId}", conn.ConnectionId);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Connect timeout ({Sec} c)",
                    Options.ConnectTimeout.TotalSeconds);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка подключения к {Url}", Options.HubUrl);
                throw;
            }
        }

        private async Task CloseConnectionAsync()
        {
            HubConnection? old;
            lock (_stateLock) { old = _connection; _connection = null; }

            if (old is null) return;

            try { await old.StopAsync().ConfigureAwait(false); } catch { }
            try { await old.DisposeAsync().ConfigureAwait(false); } catch { }
        }

        // ── Handlers ─────────────────────────────────────────

        private void AttachHandlers(HubConnection conn)
        {
            conn.Reconnecting += ex =>
            {
                _logger.LogWarning("Reconnecting: {Msg}", ex?.Message ?? "—");
                return Task.CompletedTask;
            };

            conn.Reconnected += async id =>
            {
                _logger.LogInformation("Reconnected: {ConnectionId}", id);

                try
                {
                    if (Options.AutoSyncCapabilities)
                        await SyncCapabilitiesAsync(_cts?.Token ?? CancellationToken.None)
                            .ConfigureAwait(false);

                    await OnReconnectedAsync(_cts?.Token ?? CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка после Reconnected");
                }
            };

            conn.Closed += ex =>
            {
                _logger.LogWarning("Closed: {Msg}", ex?.Message ?? "—");
                return Task.CompletedTask;
            };

            conn.On<CommandExecutionRequest, CommandExecutionResult>(
                nameof(IAgentClient.ExecuteCommand),
                HandleExecuteCommandAsync);

            conn.On(nameof(IAgentClient.RequestCapabilities),
                HandleRequestCapabilitiesAsync);

            conn.On<ConfigurationUpdate>(
                nameof(IAgentClient.UpdateConfiguration),
                HandleUpdateConfigurationAsync);
        }

        // ── ExecuteCommand + Progress ────────────────────────

        private async Task<CommandExecutionResult> HandleExecuteCommandAsync(
            CommandExecutionRequest request)
        {
            Interlocked.Increment(ref _runningCommands);

            try
            {
                if (request is null)
                    return CommandExecutionResult.Fail(
                        Guid.Empty, default, "invalid_request",
                        "Request is null.", TimeSpan.Zero);

                // Прогресс-репортер: fire-and-forget в hub.
                var progress = new Progress<CommandProgress>(p => _ = SendProgressSafeAsync(p));

                var context = new CommandContext
                {
                    ExecutionId = request.ExecutionId,
                    CommandId = request.CommandId,
                    RawArgs = request.RawArgs,
                    NamedArgs = request.NamedArgs,
                    CallerId = request.CallerId,
                    CallerLogin = request.CallerLogin,
                    Progress = progress,
                };

                var sw = Stopwatch.StartNew();

                using var cts = new CancellationTokenSource(
                    request.TimeoutSeconds is int t
                        ? TimeSpan.FromSeconds(t)
                        : Options.CommandTimeout);

                var result = await _invoker
                    .InvokeAsync(context, cts.Token)
                    .ConfigureAwait(false);

                sw.Stop();

                var execResult = result.Success
                    ? CommandExecutionResult.Ok(
                        request.ExecutionId, request.CommandId,
                        result.Output, sw.Elapsed, result.StructuredOutput)
                    : CommandExecutionResult.Fail(
                        request.ExecutionId, request.CommandId,
                        result.ErrorCode ?? "execution_failed",
                        result.ErrorMessage ?? "Ошибка выполнения.",
                        sw.Elapsed);

                await ReportExecutionAsync(execResult).ConfigureAwait(false);
                await OnCommandExecutedAsync(request, execResult, CancellationToken.None)
                    .ConfigureAwait(false);

                return execResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Критическая ошибка обработки команды {CommandId}",
                    request?.CommandId.Value ?? "—");

                return CommandExecutionResult.Fail(
                    request?.ExecutionId ?? Guid.Empty,
                    request?.CommandId ?? default,
                    "handler_crashed", ex.Message, TimeSpan.Zero);
            }
            finally
            {
                Interlocked.Decrement(ref _runningCommands);
            }
        }

        private async Task SendProgressSafeAsync(CommandProgress progress)
        {
            try
            {
                var conn = Connection;
                if (conn is null || conn.State != HubConnectionState.Connected) return;

                await conn.InvokeAsync(nameof(IAgentHub.ReportProgress), progress)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ReportProgress failed");
            }
        }

        private async Task ReportExecutionAsync(CommandExecutionResult result)
        {
            var conn = Connection;
            if (conn is null || conn.State != HubConnectionState.Connected) return;

            try
            {
                await conn.InvokeAsync(nameof(IAgentHub.ReportExecution), result)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "ReportExecution failed");
            }
        }

        // ── Handle: RequestCapabilities ──────────────────────

        private async Task HandleRequestCapabilitiesAsync()
        {
            try
            {
                await SyncCapabilitiesAsync(_cts?.Token ?? CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "RequestCapabilities handling failed");
            }
        }

        // ── Handle: UpdateConfiguration ──────────────────────

        private async Task HandleUpdateConfigurationAsync(ConfigurationUpdate update)
        {
            try
            {
                if (update is null) return;

                await OnConfigurationUpdateAsync(update, _cts?.Token ?? CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "UpdateConfiguration handling failed");
            }
        }

        // ── Hooks ────────────────────────────────────────────

        protected virtual Task OnReconnectedAsync(CancellationToken ct)
            => Task.CompletedTask;

        protected virtual Task OnCapabilitiesSyncedAsync(
            IReadOnlyList<CapabilityDto> sent,
            CapabilitySyncResponse? response,
            CancellationToken ct)
            => Task.CompletedTask;

        protected virtual Task OnCommandExecutedAsync(
            CommandExecutionRequest request,
            CommandExecutionResult result,
            CancellationToken ct)
            => Task.CompletedTask;

        protected virtual Task OnConfigurationUpdateAsync(
            ConfigurationUpdate update,
            CancellationToken ct)
            => Task.CompletedTask;

        // ── Helpers ──────────────────────────────────────────

        protected virtual Assembly[] ResolveAssemblies()
        {
            if (Options.CommandAssemblies is { Length: > 0 })
                return Options.CommandAssemblies;

            var entry = Assembly.GetEntryAssembly();
            return entry is not null ? new[] { entry } : Array.Empty<Assembly>();
        }

        protected virtual string ResolveClientVersion()
            => Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";

        protected void ThrowIfDisposed()
        {
            if (_disposedFlag) throw new ObjectDisposedException(GetType().Name);
        }
    }
}