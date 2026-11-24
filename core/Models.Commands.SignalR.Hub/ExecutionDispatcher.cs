using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Hub;

public sealed class ExecutionRoutingException : Exception
{
    public CommandId CommandId { get; }

    public ExecutionRoutingException(CommandId commandId, string message)
        : base(message) => CommandId = commandId;
}

public sealed class ExecutionDispatcher<THub> : IExecutionDispatcher
    where THub : Hub<IAgentClient>
{
    private readonly IHubContext<THub, IAgentClient> _hub;
    private readonly IClientRegistry _registry;
    private readonly ICapabilityStore _capabilities;
    private readonly ILogger<ExecutionDispatcher<THub>> _log;

    // ExecutionId → (tcs, progress)
    private readonly ConcurrentDictionary<
        Guid,
        (Guid ClientId, string ConnectionId, CommandId CommandId, TaskCompletionSource<CommandExecutionResult> Tcs, IProgress<CommandProgress>? Progress)> _pending = new();

    public int PendingCount => _pending.Count;

    public ExecutionDispatcher(
        IHubContext<THub, IAgentClient> hub,
        IClientRegistry registry,
        ICapabilityStore capabilities,
        ILogger<ExecutionDispatcher<THub>> log)
    {
        _hub = hub;
        _registry = registry;
        _capabilities = capabilities;
        _log = log;
    }

    public async Task<CommandExecutionResult> DispatchAsync(
        Guid clientId,
        CommandExecutionRequest request,
        TimeSpan timeout,
        IProgress<CommandProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (request is null)
            return CommandExecutionResult.Fail(
                Guid.Empty, default, "invalid_request", "Request is null.", TimeSpan.Zero);

        var client = await _registry.GetByIdAsync(clientId, ct).ConfigureAwait(false);

        if (client is null)
            return Failure(request, "client_not_found", $"Клиент {clientId} не найден.");

        if (!client.Online || string.IsNullOrEmpty(client.ConnectionId))
            return Failure(request, "client_offline", $"Клиент {clientId} offline.");

        var tcs = new TaskCompletionSource<CommandExecutionResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        if (request.ExecutionId == Guid.Empty || request.CommandId.IsEmpty)
            return Failure(request, "invalid_request", "ExecutionId and CommandId are required.");

        if (!_pending.TryAdd(request.ExecutionId, (clientId, client.ConnectionId, request.CommandId, tcs, progress)))
            return Failure(request, "duplicate_execution",
                $"ExecutionId {request.ExecutionId} уже выполняется.");

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);

            using var reg = timeoutCts.Token.Register(() =>
            {
                if (_pending.TryRemove(request.ExecutionId, out var p))
                    p.Tcs.TrySetResult(Failure(request, "timeout",
                        $"Клиент не ответил за {timeout.TotalSeconds:F1} c."));
            });

            try
            {
                await _hub.Clients
                    .Client(client.ConnectionId)
                    .ExecuteCommand(request)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Ошибка отправки {CommandId} клиенту {ClientId}",
                    request.CommandId.Value, clientId);
                _pending.TryRemove(request.ExecutionId, out _);
                return Failure(request, "send_failed", ex.Message);
            }

            return await tcs.Task.ConfigureAwait(false);
        }
        finally { _pending.TryRemove(request.ExecutionId, out _); }
    }

    public async Task<CommandExecutionResult> DispatchToAnyAsync(
        CommandExecutionRequest request,
        TimeSpan timeout,
        IProgress<CommandProgress>? progress = null,
        CancellationToken ct = default)
    {
        if (request is null)
            return CommandExecutionResult.Fail(
                Guid.Empty, default, "invalid_request", "Request is null.", TimeSpan.Zero);

        var client = await _capabilities.FindClientAsync(request.CommandId, ct)
            .ConfigureAwait(false);

        if (client is null)
            throw new ExecutionRoutingException(
                request.CommandId,
                $"Нет онлайн-клиента, умеющего '{request.CommandId.Value}'.");

        return await DispatchAsync(client.ClientId, request, timeout, progress, ct)
            .ConfigureAwait(false);
    }

    public bool CompleteFrom(Guid clientId, string connectionId, CommandExecutionResult result)
    {
        if (result is null || result.ExecutionId == Guid.Empty) return false;
        if (!_pending.TryGetValue(result.ExecutionId, out var p) ||
            p.ClientId != clientId ||
            p.ConnectionId != connectionId ||
            p.CommandId != result.CommandId) return false;
        if (!_pending.TryRemove(result.ExecutionId, out var removed)) return false;
        removed.Tcs.TrySetResult(result);
        return true;
    }

    public bool ReportFrom(Guid clientId, string connectionId, CommandProgress progress)
    {
        if (progress is null || progress.ExecutionId == Guid.Empty) return false;
        if (!_pending.TryGetValue(progress.ExecutionId, out var p) ||
            p.ClientId != clientId || p.ConnectionId != connectionId) return false;
        try { p.Progress?.Report(progress); }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Ошибка IProgress.Report для {ExecutionId}", progress.ExecutionId);
        }
        return true;
    }

    private static CommandExecutionResult Failure(
        CommandExecutionRequest request, string code, string message)
        => CommandExecutionResult.Fail(
            request.ExecutionId, request.CommandId, code, message, TimeSpan.Zero);
}