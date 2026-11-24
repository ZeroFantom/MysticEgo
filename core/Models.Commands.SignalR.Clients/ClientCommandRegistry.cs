using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Clients;

public sealed class ClientCommandRegistry : IClientCommandRegistry
{
    private readonly Assembly[] _assemblies;
    private readonly ILogger<ClientCommandRegistry>? _log;
    private readonly ConcurrentDictionary<CommandId, Type> _map = new();

    public ClientCommandRegistry(
        Assembly[] assemblies,
        ILogger<ClientCommandRegistry>? log = null)
    {
        _assemblies = assemblies ?? Array.Empty<Assembly>();
        _log = log;
        Rescan();
    }

    public IReadOnlyCollection<CommandId> AllIds => _map.Keys.ToArray();

    public Type? FindType(CommandId id)
    {
        if (id.IsEmpty) return null;
        return _map.TryGetValue(id, out var t) ? t : null;
    }

    public void Rescan()
    {
        _map.Clear();

        foreach (var asm in _assemblies)
        {
            if (asm is null) continue;
            try { ScanAssembly(asm); }
            catch (Exception ex)
            {
                _log?.LogWarning(ex,
                    "Ошибка сканирования сборки {Assembly}", asm.GetName().Name);
            }
        }

        _log?.LogInformation(
            "ClientCommandRegistry: зарегистрировано {Count} команд", _map.Count);
    }

    private void ScanAssembly(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type is null) continue;
            if (!type.IsClass || type.IsAbstract) continue;
            if (!CommandMetadata.IsCommand(type)) continue;

            

            if (type.Name is "UnknownCommand" or "RejectedCommand")
                continue;

            try
            {
                var id = CommandMetadata.GetId(type);

                if (id.IsEmpty) continue;

                if (_map.TryGetValue(id, out var existing) && existing != type)
                {
                    _log?.LogWarning(
                        "CommandId '{Id}' определён дважды: {A} и {B}. Регистрация конфликтующей команды отклонена.",
                        id.Value, existing.FullName, type.FullName);
                    continue;
                }

                _map[id] = type;
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex,
                    "Не удалось зарегистрировать тип {Type}", type.FullName);
            }
        }
    }
}