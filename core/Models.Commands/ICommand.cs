namespace Models.Commands;

/// <summary>
/// Контракт исполняемой команды клиента.
/// Принимает CommandContext (id, args, progress).
/// Возвращает CommandResult (домен, без ExecutionId).
/// </summary>
public interface ICommand
{
    Task<CommandResult> ExecuteAsync(
        CommandContext context,
        CancellationToken cancellationToken = default);
}