using Microsoft.Extensions.Logging;
using Models.Commands;
using Models.Commands.Scanner;
using Models.Commands.SignalR;
using Models.Commands.SignalR.Clients;
using Models.Commands.SignalR.Hub;
using Xunit;
using Xunit.Abstractions;

namespace TestMagicCommands;

/// <summary>
/// Интеграционный тест связки:
///   1. CapabilityScanner   — находит команды через рефлексию.
///   2. ICapabilityStore    — хранит capabilities по клиентам.
///   3. ICommandInvoker     — выполняет команду по CommandContext.
///
/// Проверяет, что CommandId из сканера совпадает с CommandId,
/// который ищет сервер, и что вызов по нему действительно
/// приводит к выполнению нужной команды.
/// </summary>
public class CapabilityPipelineTests
{
    private readonly ITestOutputHelper _output;

    public CapabilityPipelineTests(ITestOutputHelper output)
        => _output = output;

    // ════════════════════════════════════════════════════════════════
    //  TestLogger — пишет всё в вывод xUnit.
    //  Без него сканер молчит и глотает ошибки.
    // ════════════════════════════════════════════════════════════════

    private sealed class TestLogger<T> : ILogger<T>
    {
        private readonly ITestOutputHelper _out;
        private readonly string _category;

        public TestLogger(ITestOutputHelper output, string? category = null)
        {
            _out = output;
            _category = category ?? typeof(T).Name;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = formatter(state, exception);
            _out.WriteLine($"[{logLevel}] {_category}: {message}");

            if (exception is not null)
                _out.WriteLine($"  ↳ {exception.GetType().Name}: {exception.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Тестовые команды
    // ════════════════════════════════════════════════════════════════

    [CommandDescription("Список активных аудиоустройств")]
    public sealed class AudioListCommand : CommandBase
    {
        protected override Task<CommandResult> ExecuteCoreAsync(
            CommandContext ctx, CancellationToken ct)
        {
            ctx.Report("query", 50, "reading devices");
            return Task.FromResult(CommandResult.Ok("devices: [mic, speakers]"));
        }
    }
    
    public sealed class AudioPlayCommand : CommandBase
    {
        private readonly string? _tail;

        public AudioPlayCommand(string? tail) => _tail = tail;

        protected override Task<CommandResult> ExecuteCoreAsync(
            CommandContext ctx, CancellationToken ct)
        {
            ctx.Report("play", 100);
            return Task.FromResult(CommandResult.Ok($"playing: {_tail ?? "default"}"));
        }
    }

    public sealed class RgbMouseCommand : CommandBase
    {
        private readonly string? _tail;

        public RgbMouseCommand(string? tail) => _tail = tail;

        protected override Task<CommandResult> ExecuteCoreAsync(
            CommandContext ctx, CancellationToken ct)
        {
            return Task.FromResult(CommandResult.Ok($"mouse color: {_tail ?? "unset"}"));
        }
    }

    public sealed class RgbKeyboardCommand : CommandBase
    {
        protected override Task<CommandResult> ExecuteCoreAsync(
            CommandContext ctx, CancellationToken ct)
        {
            return Task.FromResult(CommandResult.Ok("keyboard color set"));
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  FakeClientRegistry
    // ════════════════════════════════════════════════════════════════

    private sealed class FakeClientRegistry : IClientRegistry
    {
        private readonly Dictionary<Guid, ClientRecord> _byId = new();
        private readonly Dictionary<string, Guid> _byConn = new();

        public Task<ClientRecord> RegisterAsync(ClientRecord record, CancellationToken ct = default)
        {
            _byId[record.ClientId] = record;
            _byConn[record.ConnectionId] = record.ClientId;
            return Task.FromResult(record);
        }

        public Task UnregisterAsync(string connectionId, CancellationToken ct = default)
        {
            if (_byConn.TryGetValue(connectionId, out var id))
            {
                _byId[id] = _byId[id] with { Online = false };
                _byConn.Remove(connectionId);
            }
            return Task.CompletedTask;
        }

        public Task<ClientRecord?> GetByIdAsync(Guid clientId, CancellationToken ct = default)
        {
            _byId.TryGetValue(clientId, out var r);
            return Task.FromResult(r);
        }

        public Task<ClientRecord?> GetByConnectionAsync(string connectionId, CancellationToken ct = default)
        {
            if (_byConn.TryGetValue(connectionId, out var id))
                return Task.FromResult<ClientRecord?>(_byId[id]);

            return Task.FromResult<ClientRecord?>(null);
        }

        public Task<IReadOnlyList<ClientRecord>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ClientRecord>>(_byId.Values.ToList());

        public Task<int> CountOnlineAsync(CancellationToken ct = default)
            => Task.FromResult(_byId.Values.Count(c => c.Online));

        public Task TouchAsync(Guid clientId, HeartbeatReport report, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task UpdateCapabilityCountAsync(Guid clientId, int count, CancellationToken ct = default)
        {
            if (_byId.TryGetValue(clientId, out var r))
                _byId[clientId] = r with { CapabilityCount = count };
            return Task.CompletedTask;
        }
    }

    // ════════════════════════════════════════════════════════════════
    //  Pipeline — теперь с логгером
    // ════════════════════════════════════════════════════════════════

    private sealed class Pipeline
    {
        public Guid ClientId { get; } = Guid.NewGuid();
        public string ConnectionId { get; } = "conn-1";
        public string MachineName { get; } = "test-machine";

        public IClientRegistry Registry { get; }
        public ICapabilityStore Capabilities { get; }
        public ICapabilityScanner Scanner { get; }
        public IClientCommandRegistry CommandRegistry { get; }
        public ICommandInvoker Invoker { get; }

        public Pipeline(ITestOutputHelper output)
        {
            Registry = new FakeClientRegistry();
            Capabilities = new InMemoryCapabilityStore(Registry);

            // Логгер видно в выводе теста
            Scanner = new CapabilityScanner(
                log: new TestLogger<CapabilityScanner>(output, "Scanner"));

            CommandRegistry = new ClientCommandRegistry(
                new[] { typeof(Pipeline).Assembly },
                new TestLogger<ClientCommandRegistry>(output, "Registry"));

            Invoker = new DefaultCommandInvoker(
                CommandRegistry,
                new EmptyServiceProvider(),
                new TestLogger<DefaultCommandInvoker>(output, "Invoker"));
        }

        public async Task RegisterClientAsync()
        {
            var record = new ClientRecord
            {
                ClientId = ClientId,
                ConnectionId = ConnectionId,
                MachineName = MachineName,
                Version = "1.0.0",
                Online = true,
            };

            await Registry.RegisterAsync(record);

            var caps = Scanner.Scan(typeof(Pipeline).Assembly);
            await Capabilities.ReplaceAsync(ClientId, caps);
            await Registry.UpdateCapabilityCountAsync(ClientId, caps.Count);
        }
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    // ════════════════════════════════════════════════════════════════
    //  ТЕСТЫ
    // ════════════════════════════════════════════════════════════════

    [Fact]
    public void Diagnostic_WhyScannerReturnsNothing()
    {
        var asm = typeof(Pipeline).Assembly;

        var allTypes = asm.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract)
            .Where(t => t.Name.EndsWith("Command"))
            .ToList();

        _output.WriteLine($"Найдено типов с суффиксом Command: {allTypes.Count}");

        foreach (var t in allTypes)
        {
            bool isICommand = typeof(ICommand).IsAssignableFrom(t);
            _output.WriteLine($"  {t.FullName}  isICommand={isICommand}");
        }

        // Прогоняем сканер с логгером — если он упадёт, увидим в выводе
        var scanner = new CapabilityScanner(
            log: new TestLogger<CapabilityScanner>(_output, "Scanner"));

        var caps = scanner.Scan(asm);

        _output.WriteLine($"");
        _output.WriteLine($"Сканер вернул: {caps.Count} capabilities");

        foreach (var c in caps)
            _output.WriteLine($"  {c.Id.Value} — {c.DisplayName}");

        Assert.True(true);
    }

    [Fact]
    public void Pipeline_Scanner_FindsTestCommands()
    {
        var pipeline = new Pipeline(_output);

        var caps = pipeline.Scanner.Scan(typeof(Pipeline).Assembly);

        _output.WriteLine($"Сканер вернул {caps.Count} capabilities:");
        foreach (var c in caps)
            _output.WriteLine($"  {c.Id.Value} — {c.Description}");

        var ids = caps.Select(c => c.Id.Value).ToHashSet();

        Assert.Contains("audio.list",   ids);
        Assert.Contains("audio.play",   ids);
        Assert.Contains("rgb.mouse",    ids);
        Assert.Contains("rgb.keyboard", ids);
    }

    [Fact]
    public async Task Pipeline_ClientRegister_PublishesCapabilities()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var caps = await pipeline.Capabilities.GetAsync(pipeline.ClientId);
        Assert.Equal(4, caps.Count);

        var record = await pipeline.Registry.GetByIdAsync(pipeline.ClientId);
        Assert.NotNull(record);
        Assert.Equal(4, record!.CapabilityCount);
    }

    [Fact]
    public async Task Pipeline_FindClient_ByCommandId_Works()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var client = await pipeline.Capabilities
            .FindClientAsync(CommandId.FromName("rgb.mouse"));

        Assert.NotNull(client);
        Assert.Equal(pipeline.ClientId, client!.ClientId);
    }

    [Fact]
    public async Task Pipeline_FindClient_ForUnknownCommand_ReturnsNull()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var client = await pipeline.Capabilities
            .FindClientAsync(CommandId.FromName("nonexistent.command"));

        Assert.Null(client);
    }

    [Fact]
    public async Task Pipeline_Invoke_NoArgsCommand_ReturnsExpectedOutput()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var ctx = new CommandContext
        {
            ExecutionId = Guid.NewGuid(),
            CommandId = CommandId.FromName("audio.list"),
        };

        var result = await pipeline.Invoker.InvokeAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal("devices: [mic, speakers]", result.Output);
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task Pipeline_Invoke_CommandWithTail_PassesRawArgs()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var ctx = new CommandContext
        {
            ExecutionId = Guid.NewGuid(),
            CommandId = CommandId.FromName("rgb.mouse"),
            RawArgs = "red",
        };

        var result = await pipeline.Invoker.InvokeAsync(ctx);

        Assert.True(result.Success);
        Assert.Equal("mouse color: red", result.Output);
    }

    [Fact]
    public async Task Pipeline_Invoke_UnknownCommand_ReturnsFail()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var ctx = new CommandContext
        {
            ExecutionId = Guid.NewGuid(),
            CommandId = CommandId.FromName("not.exists"),
        };

        var result = await pipeline.Invoker.InvokeAsync(ctx);

        Assert.False(result.Success);
        Assert.Equal("command_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task Pipeline_Invoke_CapturesProgress()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var progressEvents = new List<CommandProgress>();
        var progress = new SyncProgress<CommandProgress>(p => progressEvents.Add(p));

        var ctx = new CommandContext
        {
            ExecutionId = Guid.NewGuid(),
            CommandId = CommandId.FromName("audio.play"),
            RawArgs = "rock.mp3",
            Progress = progress,
        };

        var result = await pipeline.Invoker.InvokeAsync(ctx);

        Assert.True(result.Success);

        _output.WriteLine($"Собрано {progressEvents.Count} событий:");
        foreach (var p in progressEvents)
            _output.WriteLine($"  [{p.Stage}] {p.Percent}% — {p.Message}");

        Assert.Contains(progressEvents, p => p.Stage == "start");
        Assert.Contains(progressEvents, p => p.Stage == "play");
        Assert.Contains(progressEvents, p => p.Stage == "done");
        Assert.All(progressEvents, p => Assert.Equal(ctx.ExecutionId, p.ExecutionId));
    }

    [Fact]
    public async Task Pipeline_TwoClients_WithSameCapability_FirstWins()
    {
        var registry = new FakeClientRegistry();
        var store = new InMemoryCapabilityStore(registry);

        var clientA = Guid.NewGuid();
        var clientB = Guid.NewGuid();

        await registry.RegisterAsync(new ClientRecord
        {
            ClientId = clientA, ConnectionId = "conn-a", Online = true,
        });
        await registry.RegisterAsync(new ClientRecord
        {
            ClientId = clientB, ConnectionId = "conn-b", Online = true,
        });

        var caps = new[]
        {
            new CapabilityDto
            {
                Id = CommandId.FromName("audio.list"),
                Description = "list",
            },
        };

        await store.ReplaceAsync(clientA, caps);
        await store.ReplaceAsync(clientB, caps);

        var all = await store.FindAllClientsAsync(CommandId.FromName("audio.list"));
        Assert.Equal(2, all.Count);

        var first = await store.FindClientAsync(CommandId.FromName("audio.list"));
        Assert.NotNull(first);
    }

    [Fact]
    public async Task Pipeline_OfflineClient_NotReturned()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        await pipeline.Registry.UnregisterAsync(pipeline.ConnectionId);

        var client = await pipeline.Capabilities
            .FindClientAsync(CommandId.FromName("rgb.mouse"));

        Assert.Null(client);
    }

    [Fact]
    public async Task Pipeline_ReplaceCapabilities_Overwrites()
    {
        var pipeline = new Pipeline(_output);
        await pipeline.RegisterClientAsync();

        var before = await pipeline.Capabilities.GetAsync(pipeline.ClientId);
        Assert.Equal(4, before.Count);

        await pipeline.Capabilities.ReplaceAsync(pipeline.ClientId, new[]
        {
            new CapabilityDto
            {
                Id = CommandId.FromName("audio.list"),
                Description = "only one",
            },
        });

        var after = await pipeline.Capabilities.GetAsync(pipeline.ClientId);
        Assert.Single(after);
        Assert.Equal("audio.list", after[0].Id.Value);
    }

    // ════════════════════════════════════════════════════════════════
    //  SyncProgress<T> — синхронный IProgress для тестов.
    // ════════════════════════════════════════════════════════════════

    private sealed class SyncProgress<T> : IProgress<T>
    {
        private readonly Action<T> _action;
        public SyncProgress(Action<T> action) => _action = action;
        public void Report(T value) => _action(value);
    }
}