using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Hub
{
    /// <summary>
    /// Базовый SignalR-хаб для админки. Наследуй и расширяй.
    ///
    /// Админка вызывает методы — сервер делает запросы в реестр,
    /// хранилище capabilities и диспетчер.
    ///
    /// Все методы защищены от падения — ошибки логируются,
    /// возвращаются корректные пустые значения.
    /// </summary>
    public abstract class AdminHubBase : Hub<IAdminClient>, IAdminHub
    {
        protected IClientRegistry Registry { get; }
        protected ICapabilityStore Capabilities { get; }
        protected IExecutionDispatcher Dispatcher { get; }
        protected IHubErrorHandler ErrorHandler { get; }
        protected ILogger Logger { get; }

        protected AdminHubBase(
            IClientRegistry registry,
            ICapabilityStore capabilities,
            IExecutionDispatcher dispatcher,
            IHubErrorHandler errorHandler,
            ILogger logger)
        {
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
            Dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            ErrorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ── Lifecycle ────────────────────────────────────────

        public override async Task OnConnectedAsync()
        {
            try
            {
                Logger.LogInformation(
                    "Admin подключён: {User} (conn={ConnectionId})",
                    Context.User?.Identity?.Name ?? "anonymous",
                    Context.ConnectionId);

                await OnAdminConnectedAsync(Context.ConnectionAborted).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(OnConnectedAsync), ex);
            }

            try
            {
                await base.OnConnectedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(OnConnectedAsync) + ".base", ex);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            try
            {
                Logger.LogInformation(
                    "Admin отключён: {User} ({Reason})",
                    Context.User?.Identity?.Name ?? "anonymous",
                    exception?.Message ?? "нормальное закрытие");

                await OnAdminDisconnectedAsync(exception, Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(OnDisconnectedAsync), ex);
            }

            try
            {
                await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(OnDisconnectedAsync) + ".base", ex);
            }
        }

        // ── IAdminHub: ListClients ───────────────────────────

        public virtual async Task<IReadOnlyList<ClientSummaryDto>> ListClients()
        {
            try
            {
                var records = await Registry
                    .ListAsync(Context.ConnectionAborted)
                    .ConfigureAwait(false);

                return records.Select(r => r.ToSummary()).ToList();
            }
            catch (Exception ex)
            {
                HandleError(nameof(ListClients), ex);
                return Array.Empty<ClientSummaryDto>();
            }
        }

        // ── IAdminHub: GetCapabilities ───────────────────────

        public virtual async Task<IReadOnlyList<CapabilityDto>> GetCapabilities(Guid clientId)
        {
            try
            {
                if (clientId == Guid.Empty)
                    return Array.Empty<CapabilityDto>();

                return await Capabilities
                    .GetAsync(clientId, Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(GetCapabilities), ex);
                return Array.Empty<CapabilityDto>();
            }
        }

        // ── IAdminHub: ExecuteOnClient ───────────────────────

        public virtual async Task<CommandExecutionResult> ExecuteOnClient(
            Guid clientId, CommandExecutionRequest request)
        {
            try
            {
                if (request is null)
                    return CommandExecutionResult.Fail(
                        Guid.Empty, default, "invalid_request", "Request is null.", TimeSpan.Zero);

                var timeout = request.TimeoutSeconds is int t
                    ? TimeSpan.FromSeconds(t)
                    : DefaultExecutionTimeout;

                return await Dispatcher
                    .DispatchAsync(clientId, request, timeout, progress: null,
                        Context.ConnectionAborted)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(ExecuteOnClient), ex);
                return CommandExecutionResult.Fail(
                    request?.ExecutionId ?? Guid.Empty,
                    request?.CommandId ?? default,
                    "dispatcher_error", ex.Message, TimeSpan.Zero);
            }
        }

        // ── Push в админку ──────────────────────────────────

        /// <summary>
        /// Уведомить всех подключённых админов о чём-либо.
        /// </summary>
        protected async Task BroadcastAsync(
            Func<IAdminClient, Task> action, CancellationToken ct = default)
        {
            try
            {
                await action(Clients.All).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                HandleError(nameof(BroadcastAsync), ex);
            }
        }

        // ── Hooks ───────────────────────────────────────────

        protected virtual Task OnAdminConnectedAsync(CancellationToken ct)
            => Task.CompletedTask;

        protected virtual Task OnAdminDisconnectedAsync(Exception? exception, CancellationToken ct)
            => Task.CompletedTask;

        // ── Настройки ───────────────────────────────────────

        /// <summary>Таймаут выполнения команды по умолчанию. Переопределяется в наследнике.</summary>
        protected virtual TimeSpan DefaultExecutionTimeout => TimeSpan.FromSeconds(60);

        // ── Error handling ──────────────────────────────────

        protected void HandleError(string method, Exception ex)
        {
            ErrorHandler.Handle(new HubErrorContext
            {
                HubName = GetType().Name,
                Method = method,
                ConnectionId = Context?.ConnectionId,
                ClientId = null,
                Exception = ex,
            });
        }
    }
}