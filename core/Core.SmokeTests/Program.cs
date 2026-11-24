using Models.Commands;
using Models.Data;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception(message);
}

Check(CommandMetadata.GetId(typeof(PingCommand)).Value == "test.ping", "Explicit command id failed");
Check(CommandMetadata.GetVersion(typeof(PingCommand)) == 2, "Command version failed");
Check(!CommandMetadata.IsCommand(typeof(HiddenCommand)), "SkipCommand failed");

var path = Path.Combine(Path.GetTempPath(), "mysticego-smoke-" + Guid.NewGuid().ToString("N") + ".db");
try
{
    var store = new DynamicEntityStore($"Data Source={path}");
    var id = Guid.NewGuid();
    await store.PutAsync(new DemoComputer { Id = id, Name = "PC-01" });
    var loaded = await store.GetAsync<DemoComputer>(id);
    Check(loaded?.Name == "PC-01", "Round-trip failed");
    await store.PutAsync(new DemoComputer { Id = id, Name = "PC-02" });
    loaded = await store.GetAsync<DemoComputer>(id);
    Check(loaded?.Name == "PC-02", "Upsert failed");
    Check(await store.DeleteAsync<DemoComputer>(id), "Delete failed");
    Check(await store.GetAsync<DemoComputer>(id) is null, "Deleted item still exists");
    Console.WriteLine("Быстрые проверки ядра: ПРОЙДЕНЫ");
}
finally
{
    if (File.Exists(path)) File.Delete(path);
}

[CommandContract("test.ping", version: 2)]
public sealed class PingCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken ct = default) =>
        Task.FromResult(CommandResult.Ok("pong"));
}

[SkipCommand]
public sealed class HiddenCommand : ICommand
{
    public Task<CommandResult> ExecuteAsync(CommandContext context, CancellationToken ct = default) =>
        Task.FromResult(CommandResult.Ok());
}

[Entity("club.computer", version: 1)]
public sealed class DemoComputer
{
    [EntityKey] public Guid Id { get; set; }
    [EntityField(Required = true)] public string Name { get; set; } = "";
}
