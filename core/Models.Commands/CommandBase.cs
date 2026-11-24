namespace Models.Commands;

/// <summary>
/// Базовый класс команды. Держит try/catch, проверки,
/// отчёт о прогрессе (start/done/error).
///
/// Наследник реализует только ExecuteCoreAsync.
/// </summary>
public abstract class CommandBase : ICommand
{
    /// <summary>Требуется ли ClientInfo (индивидуальные настройки).</summary>
    protected virtual bool RequiresClientInfo => true;

    public async Task<CommandResult> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken = default)
    {
        if (RequiresClientInfo && !HasClientInfo())
        {
            context.Report("error", null, "ClientInfo недоступен");
            return CommandResult.Fail(
                "no_client_info",
                "У клиента отсутствуют индивидуальные настройки.");
        }

        context.Report("start", 0);

        try
        {
            var result = await ExecuteCoreAsync(context, cancellationToken)
                .ConfigureAwait(false);

            if (result.Success)
                context.Report("done", 100);
            else
                context.Report("error", null, result.ErrorMessage);

            return result;
        }
        catch (OperationCanceledException)
        {
            context.Report("cancelled", null, "Отменено");
            return CommandResult.Fail("cancelled", "Команда отменена.");
        }
        catch (Exception ex)
        {
            context.Report("error", null, ex.Message);
            OnUnhandledException(context, ex);
            return CommandResult.Fail("execution_failed", ex.Message);
        }
    }

    protected abstract Task<CommandResult> ExecuteCoreAsync(
        CommandContext context,
        CancellationToken cancellationToken);

    /// <summary>
    /// Hook для логирования необработанных ошибок.
    /// Переопредели, если хочешь свою политику (например, LoggerService).
    /// </summary>
    protected virtual void OnUnhandledException(CommandContext context, Exception ex)
    {
        // По умолчанию — ничего.
    }

    /// <summary>
    /// Проверка наличия ClientInfo.
    /// Переопредели, если источник данных другой.
    /// </summary>
    protected virtual bool HasClientInfo() => true;
}