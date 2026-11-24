using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Clients;

public sealed class DefaultCommandInvoker : ICommandInvoker
{
    private readonly IClientCommandRegistry _registry;
    private readonly IServiceProvider _services;
    private readonly ILogger<DefaultCommandInvoker> _log;

    public DefaultCommandInvoker(
        IClientCommandRegistry registry,
        IServiceProvider services,
        ILogger<DefaultCommandInvoker> log)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<CommandResult> InvokeAsync(
        CommandContext context,
        CancellationToken ct = default)
    {
        if (context is null)
            return CommandResult.Fail("invalid_request", "Context is null.");

        try
        {
            var type = _registry.FindType(context.CommandId);

            if (type is null)
            {
                _log.LogWarning(
                    "Команда не найдена: {CommandId} (exec {ExecutionId})",
                    context.CommandId.Value, context.ExecutionId);

                return CommandResult.Fail(
                    "command_not_found",
                    $"Команда '{context.CommandId.Value}' не зарегистрирована.");
            }

            var instance = CreateInstance(type, context.RawArgs);

            if (instance is null)
            {
                _log.LogWarning(
                    "Не удалось создать {Type} (exec {ExecutionId})",
                    type.FullName, context.ExecutionId);

                return CommandResult.Fail(
                    "instantiation_failed",
                    $"Не удалось создать экземпляр {type.FullName}.");
            }

            _log.LogInformation(
                "Выполняю {CommandId} (exec {ExecutionId})",
                context.CommandId.Value, context.ExecutionId);

            try
            {
                var result = await instance
                    .ExecuteAsync(context, ct)
                    .ConfigureAwait(false);

                return result ?? CommandResult.Fail(
                    "empty_result", "Команда вернула null.");
            }
            catch (OperationCanceledException)
            {
                return CommandResult.Fail("cancelled", "Команда отменена.");
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Ошибка выполнения {CommandId} (exec {ExecutionId})",
                    context.CommandId.Value, context.ExecutionId);

                return CommandResult.Fail("execution_failed", ex.Message);
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "Неожиданная ошибка в invoker для {CommandId}",
                context.CommandId.Value);

            return CommandResult.Fail("internal_error", ex.Message);
        }
    }

    // ── Instance creation ───────────────────────────────

    private ICommand? CreateInstance(Type type, string? rawArgs)
    {
        // 1. DI
        try
        {
            var fromDi = _services.GetService(type) as ICommand;
            if (fromDi is not null) return fromDi;
        }
        catch { /* нет регистрации — идём дальше */ }

        // 2. ctor(string? tail)
        var ctorWithTail = type.GetConstructor(new[] { typeof(string) });
        if (ctorWithTail is not null)
        {
            try { return (ICommand)Activator.CreateInstance(type, rawArgs)!; }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "ctor(string) failed for {Type}", type.FullName);
            }
        }

        // 3. ctor()
        var ctorEmpty = type.GetConstructor(Type.EmptyTypes);
        if (ctorEmpty is not null)
        {
            try { return (ICommand)Activator.CreateInstance(type)!; }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "ctor() failed for {Type}", type.FullName);
            }
        }

        return null;
    }
}