using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Hub;

public abstract class AgentHubBase : Hub<IAgentClient>, IAgentHub
{
    protected IClientRegistry Registry { get; }
    protected ICapabilityStore Capabilities { get; }
    protected IExecutionDispatcher Dispatcher { get; }
    protected IClientIdProvider ClientIdProvider { get; }
    protected IHubErrorHandler ErrorHandler { get; }
    protected ILogger Logger { get; }

    protected AgentHubBase(
        IClientRegistry registry,
        ICapabilityStore capabilities,
        IExecutionDispatcher dispatcher,
        IClientIdProvider clientIdProvider,
        IHubErrorHandler errorHandler,
        ILogger logger)
    {
        Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        ClientIdProvider = clientIdProvider ?? throw new ArgumentNullException(nameof(clientIdProvider));
        ErrorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Lifecycle ────────────────────────────────────────

    public override async Task OnConnectedAsync()
    {
        try
        {
            var clientId = ClientIdProvider.GetClientId(Context);

            if (clientId is null)
            {
                Logger.LogWarning("OnConnected без ClientId (conn={ConnectionId})",
                    Context.ConnectionId);
                Context.Abort();
                return;
            }

            var record = new ClientRecord
            {
                ClientId = clientId.Value,
                ConnectionId = Context.ConnectionId,
                UserIdentifier = Context.UserIdentifier,
                ConnectedAt = DateTime.UtcNow,
                LastSeenAt = DateTime.UtcNow,
                Online = true,
            };

            await Registry.RegisterAsync(record, Context.ConnectionAborted).ConfigureAwait(false);

            Logger.LogInformation("Клиент подключён: id={ClientId}, conn={ConnectionId}",
                clientId.Value, Context.ConnectionId);

            await OnClientConnectedAsync(record, Context.ConnectionAborted).ConfigureAwait(false);
        }
        catch (Exception ex) { HandleError(nameof(OnConnectedAsync), null, ex); Context.Abort(); return; }

        try { await base.OnConnectedAsync().ConfigureAwait(false); }
        catch (Exception ex) { HandleError(nameof(OnConnectedAsync) + ".base", null, ex); }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var record = await Registry
                .GetByConnectionAsync(Context.ConnectionId, Context.ConnectionAborted)
                .ConfigureAwait(false);

            if (record is not null)
            {
                await Capabilities.RemoveAsync(record.ClientId, Context.ConnectionAborted)
                    .ConfigureAwait(false);

                await OnClientDisconnectedAsync(record, exception, Context.ConnectionAborted)
                    .ConfigureAwait(false);

                Logger.LogInformation(
                    "Клиент отключён: id={ClientId}, conn={ConnectionId}, reason={Reason}",
                    record.ClientId, Context.ConnectionId,
                    exception?.Message ?? "нормальное закрытие");
            }

            await Registry.UnregisterAsync(Context.ConnectionId, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
        catch (Exception ex) { HandleError(nameof(OnDisconnectedAsync), null, ex); }

        try { await base.OnDisconnectedAsync(exception).ConfigureAwait(false); }
        catch (Exception ex) { HandleError(nameof(OnDisconnectedAsync) + ".base", null, ex); }
    }

    private async Task<ClientRecord?> GetVerifiedCallerAsync()
    {
        var caller = await Registry.GetByConnectionAsync(Context.ConnectionId, Context.ConnectionAborted)
            .ConfigureAwait(false);
        var principalId = ClientIdProvider.GetClientId(Context);
        return caller is not null && principalId.HasValue && caller.ClientId == principalId.Value
            ? caller : null;
    }

    // ── SyncCapabilities ─────────────────────────────────

    public virtual async Task<CapabilitySyncResponse> SyncCapabilities(
        CapabilitySyncRequest request)
    {
        try
        {
            if (request is null) return new CapabilitySyncResponse { Accepted = 0 };

            var caller = await GetVerifiedCallerAsync().ConfigureAwait(false);
            if (caller is null) return new CapabilitySyncResponse { Accepted = 0 };
            var clientId = caller.ClientId;

            var capabilities = request.Capabilities ?? Array.Empty<CapabilityDto>();

            await Capabilities
                .ReplaceAsync(clientId, capabilities, Context.ConnectionAborted)
                .ConfigureAwait(false);

            await Registry
                .UpdateCapabilityCountAsync(clientId, capabilities.Count,
                    Context.ConnectionAborted)
                .ConfigureAwait(false);

            await OnCapabilitiesSyncedAsync(clientId, capabilities,
                Context.ConnectionAborted).ConfigureAwait(false);

            Logger.LogInformation(
                "SyncCapabilities: client={ClientId}, count={Count}, version={Version}",
                clientId, capabilities.Count, request.ClientVersion);

            return CapabilitySyncResponse.Ack(capabilities.Count, clientId);
        }
        catch (Exception ex)
        {
            HandleError(nameof(SyncCapabilities), null, ex);
            return new CapabilitySyncResponse { Accepted = 0 };
        }
    }

    // ── Heartbeat ────────────────────────────────────────

    public virtual async Task Heartbeat(HeartbeatReport report)
    {
        try
        {
            if (report is null) return;

            var caller = await GetVerifiedCallerAsync().ConfigureAwait(false);
            if (caller is null || report.ClientId != caller.ClientId) return;

            await Registry.TouchAsync(report.ClientId, report, Context.ConnectionAborted)
                .ConfigureAwait(false);

            await OnHeartbeatAsync(report, Context.ConnectionAborted).ConfigureAwait(false);
        }
        catch (Exception ex) { HandleError(nameof(Heartbeat), report?.ClientId, ex); }
    }

    // ── ReportExecution ──────────────────────────────────

    public virtual async Task ReportExecution(CommandExecutionResult result)
    {
        try
        {
            if (result is null) return;

            var caller = await GetVerifiedCallerAsync().ConfigureAwait(false);
            if (caller is null || !Dispatcher.CompleteFrom(caller.ClientId, Context.ConnectionId, result))
                return;

            await OnExecutionReportedAsync(result, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
        catch (Exception ex) { HandleError(nameof(ReportExecution), null, ex); }
    }

    // ── ReportProgress (НОВОЕ) ───────────────────────────

    public virtual async Task ReportProgress(CommandProgress progress)
    {
        try
        {
            if (progress is null) return;
            if (progress.ExecutionId == Guid.Empty) return;

            var caller = await GetVerifiedCallerAsync().ConfigureAwait(false);
            if (caller is null || !Dispatcher.ReportFrom(caller.ClientId, Context.ConnectionId, progress))
                return;

            await OnProgressReportedAsync(progress, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
        catch (Exception ex) { HandleError(nameof(ReportProgress), null, ex); }
    }

    // ── Push от сервера клиенту ──────────────────────────

    protected async Task<bool> PushConfigurationAsync(
        Guid clientId, ConfigurationUpdate update, CancellationToken ct = default)
    {
        try
        {
            var record = await Registry.GetByIdAsync(clientId, ct).ConfigureAwait(false);
            if (record is null || !record.Online) return false;

            await Clients.Client(record.ConnectionId).UpdateConfiguration(update)
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) { HandleError(nameof(PushConfigurationAsync), clientId, ex); return false; }
    }

    protected async Task<bool> RequestCapabilitiesRefreshAsync(
        Guid clientId, CancellationToken ct = default)
    {
        try
        {
            var record = await Registry.GetByIdAsync(clientId, ct).ConfigureAwait(false);
            if (record is null || !record.Online) return false;

            await Clients.Client(record.ConnectionId).RequestCapabilities()
                .ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) { HandleError(nameof(RequestCapabilitiesRefreshAsync), clientId, ex); return false; }
    }

    // ── Hooks ────────────────────────────────────────────

    protected virtual Task OnClientConnectedAsync(ClientRecord r, CancellationToken ct)
        => Task.CompletedTask;
    protected virtual Task OnClientDisconnectedAsync(ClientRecord r, Exception? ex, CancellationToken ct)
        => Task.CompletedTask;
    protected virtual Task OnCapabilitiesSyncedAsync(Guid id, IReadOnlyList<CapabilityDto> c, CancellationToken ct)
        => Task.CompletedTask;
    protected virtual Task OnHeartbeatAsync(HeartbeatReport r, CancellationToken ct)
        => Task.CompletedTask;
    protected virtual Task OnExecutionReportedAsync(CommandExecutionResult r, CancellationToken ct)
        => Task.CompletedTask;

    /// <summary>Прогресс выполнения команды. Переопредели, чтобы пушить в админку.</summary>
    protected virtual Task OnProgressReportedAsync(CommandProgress p, CancellationToken ct)
        => Task.CompletedTask;

    // ── Error ────────────────────────────────────────────

    protected void HandleError(string method, Guid? clientId, Exception ex)
    {
        ErrorHandler.Handle(new HubErrorContext
        {
            HubName = GetType().Name,
            Method = method,
            ConnectionId = Context?.ConnectionId,
            ClientId = clientId,
            Exception = ex,
        });
    }
}