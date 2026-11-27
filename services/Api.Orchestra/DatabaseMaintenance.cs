using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

// Probe independently of any browser session; never overwrite or switch the live database.
public sealed class DatabaseMaintenance(NodeState state, ILogger<DatabaseMaintenance> logger) : BackgroundService
{
    public sealed record Result(string Name, string State, string Message, DateTimeOffset CheckedAt);
    private Result current = new("database", "checking", "Ожидается первый замер базы.", DateTimeOffset.UtcNow);
    public Result Snapshot => Volatile.Read(ref current);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            var ok = false;
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            try { ok = await state.Data.CheckHealthAsync(deadline.Token); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
            catch (Exception error)
            {
                // Log only the exception type; database connection strings can contain secrets.
                if (Snapshot.State != "degraded") logger.LogWarning("Database health probe failed: {Type}", error.GetType().Name);
            }
            Volatile.Write(ref current, new Result("database", ok ? "available" : "degraded",
                ok ? state.Data.Provider + ": подключение к текущему хранилищу доступно."
                   : state.Data.Provider + ": подключение не работает. Данные и провайдер сохранены; требуется проверка или явная миграция.",
                DateTimeOffset.UtcNow));
            try { await Task.Delay(TimeSpan.FromSeconds(15), stop); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        }
    }
}
