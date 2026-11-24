namespace Models.Commands.SignalR.Clients;

/// <summary>
/// Создаёт и выполняет команду по CommandContext.
/// Возвращает доменный CommandResult (без ExecutionId).
/// Не бросает наружу — все ошибки упаковываются в CommandResult.
/// </summary>
public interface ICommandInvoker
{
    Task<CommandResult> InvokeAsync(
        CommandContext context,
        CancellationToken ct = default);
}