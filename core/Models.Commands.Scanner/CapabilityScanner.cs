using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Commands.SignalR;

namespace Models.Commands.Scanner;

/// <summary>
/// Рефлексивный сканер команд. Собирает CapabilityDto из сборок.
///
/// Гарантии:
///   • Никогда не бросает исключений при сканировании.
///   • Детерминированный порядок (сортировка по CommandId).
///   • Идемпотентность: повторный скан той же сборки — тот же результат.
///   • Каждая ошибка логируется — никогда не глотается молча.
/// </summary>
public sealed class CapabilityScanner : ICapabilityScanner
{
    private readonly CapabilityScanOptions _options;
    private readonly ILogger<CapabilityScanner> _log;

    private readonly ConcurrentDictionary<Assembly, IReadOnlyList<CapabilityDto>> _cache = new();
    private readonly ConcurrentDictionary<Assembly, XmlDocSummaryReader> _xmlCache = new();

    public CapabilityScanner(
        CapabilityScanOptions? options = null,
        ILogger<CapabilityScanner>? log = null)
    {
        _options = options ?? CapabilityScanOptions.Default;
        _log = log ?? NullLogger<CapabilityScanner>.Instance;
    }

    // ── Public API ──────────────────────────────────────

    public IReadOnlyList<CapabilityDto> Scan(Assembly assembly)
    {
        if (assembly is null) return Array.Empty<CapabilityDto>();

        if (_options.CacheResults && _cache.TryGetValue(assembly, out var cached))
        {
            _log.LogDebug("Scan({Assembly}): из кэша, {Count} capabilities",
                assembly.GetName().Name, cached.Count);
            return cached;
        }

        var result = ScanCore(assembly);

        if (_options.CacheResults)
            _cache[assembly] = result;

        return result;
    }

    public IReadOnlyList<CapabilityDto> Scan(IEnumerable<Assembly> assemblies)
    {
        if (assemblies is null) return Array.Empty<CapabilityDto>();

        var bag = new List<CapabilityDto>();
        var seen = new HashSet<CommandId>();

        foreach (var asm in assemblies)
        {
            if (asm is null) continue;

            foreach (var cap in Scan(asm))
            {
                if (seen.Add(cap.Id))
                    bag.Add(cap);
                else
                    _log.LogDebug(
                        "CommandId '{Id}' из {Assembly} уже был — пропуск дубля",
                        cap.Id.Value, asm.GetName().Name);
            }
        }

        bag.Sort((a, b) => a.Id.CompareTo(b.Id));

        _log.LogInformation(
            "Scan({AssembliesCount} assemblies): {Total} capabilities",
            assemblies.Count(), bag.Count);

        return bag;
    }

    public IReadOnlyList<CapabilityDto> ScanCurrentAppDomain()
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic)
            .Where(a => !IsSystemAssembly(a))
            .ToList();

        _log.LogInformation(
            "ScanCurrentAppDomain: {Count} сборок для сканирования", asms.Count);

        return Scan(asms);
    }

    /// <summary>Сбросить кэш (для тестов / hot-reload).</summary>
    public void ClearCache()
    {
        _cache.Clear();
        _xmlCache.Clear();
    }

    // ── Core ────────────────────────────────────────────

    private IReadOnlyList<CapabilityDto> ScanCore(Assembly assembly)
    {
        var asmName = assembly.GetName().Name ?? "?";
        var result = new List<CapabilityDto>();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).ToArray()!;

            _log.LogWarning(
                "ScanCore({Assembly}): не все типы загрузились ({Count} ошибок), " +
                "обработаю {Ok}",
                asmName, ex.LoaderExceptions.Length, types.Length);

            if (_options.VerboseDiagnostics)
            {
                foreach (var loaderEx in ex.LoaderExceptions)
                {
                    if (loaderEx is null) continue;
                    _log.LogDebug("  loader: {Message}", loaderEx.Message);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "ScanCore({Assembly}): не удалось получить типы — пропуск сборки",
                asmName);
            return result;
        }

        var version = assembly.GetName().Version?.ToString() ?? "1.0.0";
        var xml = _options.ReadXmlDoc
            ? _xmlCache.GetOrAdd(assembly, XmlDocSummaryReader.ForAssembly)
            : null;

        var candidates = 0;
        var skipped = 0;
        var failed = 0;

        foreach (var type in types)
        {
            if (type is null) continue;

            if (!IsCandidate(type))
            {
                skipped++;
                continue;
            }

            candidates++;

            try
            {
                var dto = BuildCapability(type, version, xml);
                if (dto is not null)
                {
                    result.Add(dto);

                    if (_options.VerboseDiagnostics)
                        _log.LogDebug("  ✓ {Type} → {CommandId}",
                            type.FullName, dto.Id.Value);
                }
                else
                {
                    failed++;
                    _log.LogWarning(
                        "BuildCapability({Type}) вернул null", type.FullName);
                }
            }
            catch (Exception ex)
            {
                failed++;

                _log.LogWarning(ex,
                    "BuildCapability({Type}) упал — команда пропущена",
                    type.FullName);
            }
        }

        result.Sort((a, b) => a.Id.CompareTo(b.Id));

        _log.LogInformation(
            "ScanCore({Assembly}): +{Ok} -{Failed} (всего типов {Total}, " +
            "кандидатов {Candidates}, пропущено {Skipped})",
            asmName, result.Count, failed, types.Length, candidates, skipped);

        return result;
    }

    // ── Filtering ───────────────────────────────────────

    private bool IsCandidate(Type type)
    {
        if (type is null) return false;
        if (!type.IsClass) return false;
        if (type.IsAbstract) return false;
        if (type.IsGenericTypeDefinition) return false;
        if (!typeof(ICommand).IsAssignableFrom(type)) return false;
        if (Attribute.IsDefined(type, typeof(SkipCommandAttribute), false)) return false;

        if (_options.RequireCommandSuffix &&
            !type.Name.EndsWith("Command", StringComparison.Ordinal) &&
            !Attribute.IsDefined(type, typeof(CommandContractAttribute), false))
            return false;

        if (_options.ExcludedTypeNames.Contains(type.Name)) return false;

        if (_options.AdditionalFilter is not null &&
            !_options.AdditionalFilter(type))
            return false;

        return true;
    }

    // ── BuildCapability ─────────────────────────────────

    private CapabilityDto? BuildCapability(
        Type type, string version, XmlDocSummaryReader? xml)
    {
        if (_options.VerboseDiagnostics)
            _log.LogDebug("BuildCapability: {Type}", type.FullName);

        // 1. CommandId
        var rawId = CommandMetadata.GetId(type).Value;
        if (string.IsNullOrEmpty(rawId))
        {
            _log.LogWarning(
                "BuildCapability: пустой CommandId для {Type}", type.FullName);
            return null;
        }

        var id = CommandId.FromName(rawId);

        if (_options.VerboseDiagnostics)
            _log.LogDebug("  id = '{Id}'", id.Value);

        // 2. Description
        var description = ResolveDescription(type, id, xml);
        if (string.IsNullOrWhiteSpace(description) && _options.RequireDescription)
        {
            _log.LogWarning(
                "BuildCapability: {Type} не имеет ни [Description], ни XML-дока " +
                "(RequireDescription = true)",
                type.FullName);
            return null;
        }

        // 3. Остальные поля
        var displayName = ResolveDisplayName(type, id);
        var parameters = ExtractParameters(type, xml);
        var examples = ExtractExamples(type);
        var failures = ExtractFailures(type);
        var returnType = DetectReturnType(type);
        var category = id.Category;
        var tags = BuildTags(id, type);

        return new CapabilityDto
        {
            Id = id,
            DisplayName = displayName,
            Description = description,
            Category = category,
            Version = type.GetCustomAttribute<CommandContractAttribute>() is null
                ? version : CommandMetadata.GetVersion(type).ToString(),
            ReturnType = returnType,
            Parameters = parameters,
            Examples = examples,
            Failures = failures,
            Tags = tags,
        };
    }

    // ── Description ─────────────────────────────────────

    private string ResolveDescription(Type type, CommandId id, XmlDocSummaryReader? xml)
    {
        // 1. [CommandDescription("...")]
        var own = type.GetCustomAttribute<CommandDescriptionAttribute>(inherit: false);
        if (own is not null && !string.IsNullOrWhiteSpace(own.Description))
        {
            if (_options.VerboseDiagnostics)
                _log.LogDebug("  description: из [CommandDescription]");
            return own.Description;
        }

        // 2. [Description("...")] из System.ComponentModel
        var sys = type.GetCustomAttribute<DescriptionAttribute>(inherit: false);
        if (sys is not null && !string.IsNullOrWhiteSpace(sys.Description))
        {
            if (_options.VerboseDiagnostics)
                _log.LogDebug("  description: из [Description]");
            return sys.Description;
        }

        // 3. XML-док <summary>
        if (xml is not null)
        {
            var summary = xml.GetTypeSummary(type);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                if (_options.VerboseDiagnostics)
                    _log.LogDebug("  description: из XML-дока");
                return summary;
            }
        }

        // 4. Fallback
        if (_options.VerboseDiagnostics)
            _log.LogDebug("  description: fallback");

        return $"Command '{id.Value}'.";
    }

    private static string ResolveDisplayName(Type type, CommandId id)
    {
        var attr = type.GetCustomAttribute<CommandDisplayNameAttribute>(inherit: false);
        if (attr is not null) return attr.DisplayName;

        return Humanize(id.Value);
    }

    // ── Parameters ──────────────────────────────────────

    private IReadOnlyList<ParameterDto> ExtractParameters(
        Type type, XmlDocSummaryReader? xml)
    {
        var declared = type.GetCustomAttributes<CommandArgumentAttribute>().Select(argument => new ParameterDto
        {
            Name = argument.Name, Description = argument.Description, Required = argument.Required,
            ClrType = argument.Type.FullName ?? argument.Type.Name, Format = argument.Format,
            Default = argument.Default, Values = argument.Values, Example = argument.Example
        }).ToArray();
        if (declared.Length != 0) return declared;

        var method = FindExecuteMethod(type);
        if (method is null)
        {
            if (_options.VerboseDiagnostics)
                _log.LogDebug("  parameters: execute-метод не найден");
            return Array.Empty<ParameterDto>();
        }

        var list = new List<ParameterDto>();

        foreach (var p in method.GetParameters())
        {
            // Системный параметр — не пользовательский
            if (typeof(CancellationToken).IsAssignableFrom(p.ParameterType))
                continue;

            // Системный параметр — не пользовательский
            if (typeof(CommandContext).IsAssignableFrom(p.ParameterType))
                continue;

            var clrType = p.ParameterType.FullName ?? p.ParameterType.Name;

            var description =
                p.GetCustomAttribute<DescriptionAttribute>()?.Description
                ?? (xml is not null
                    ? xml.GetParameterDescription(method, p.Name ?? "")
                    : null)
                ?? "";

            var required = IsRequired(p);
            var defaultValue = p.HasDefaultValue
                ? FormatDefaultValue(p.DefaultValue, p.ParameterType)
                : null;

            var values = p.ParameterType.IsEnum
                ? Enum.GetNames(p.ParameterType)
                : null;

            var format = CapabilityHeuristics.InferFormat(p.ParameterType, p.Name);

            list.Add(new ParameterDto
            {
                Name = p.Name ?? "arg",
                ClrType = clrType,
                Description = description,
                Required = required,
                Default = defaultValue,
                Values = values,
                Format = format,
                Example = null,
            });
        }

        if (_options.VerboseDiagnostics)
            _log.LogDebug("  parameters: {Count}", list.Count);

        return list;
    }

    private static bool IsRequired(ParameterInfo p)
    {
        if (p.HasDefaultValue) return false;

        var type = p.ParameterType;

        if (Nullable.GetUnderlyingType(type) is not null) return false;

        var nullableAttr = p.GetCustomAttribute<
            System.Runtime.CompilerServices.NullableAttribute>();

        if (nullableAttr is not null &&
            nullableAttr.NullableFlags.Length == 1)
        {
            // flag: 2 = not-null, 1 = nullable
            return nullableAttr.NullableFlags[0] == 2;
        }

        return true;
    }

    private static string? FormatDefaultValue(object? value, Type parameterType)
    {
        if (value is null) return null;

        if (value is string s) return s;
        if (value is bool b) return b ? "true" : "false";
        if (value is IFormattable f)
            return f.ToString(null, System.Globalization.CultureInfo.InvariantCulture);

        return value.ToString();
    }

    // ── Method resolution ───────────────────────────────

    private static MethodInfo? FindExecuteMethod(Type commandType)
    {
        const BindingFlags flags = BindingFlags.Instance
                                   | BindingFlags.NonPublic
                                   | BindingFlags.Public;

        var core = commandType.GetMethod("ExecuteCoreAsync", flags);
        if (core is not null && !core.IsAbstract) return core;

        var pub = commandType.GetMethod("ExecuteAsync", flags);
        if (pub is not null && !pub.IsAbstract) return pub;

        return null;
    }

    private static string DetectReturnType(Type commandType)
    {
        var method = FindExecuteMethod(commandType);
        if (method is null) return "System.String";

        var rt = method.ReturnType;

        if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var inner = rt.GetGenericArguments()[0];
            return inner.FullName ?? inner.Name;
        }

        if (rt == typeof(Task)) return "System.Void";

        return rt.FullName ?? rt.Name;
    }

    // ── Examples / Failures ─────────────────────────────

    private static IReadOnlyList<ExampleDto> ExtractExamples(Type type)
    {
        var attrs = type.GetCustomAttributes<CommandExampleAttribute>(inherit: false);

        var list = new List<ExampleDto>();
        foreach (var a in attrs)
            list.Add(new ExampleDto(a.Invocation, a.Description));

        return list;
    }

    private static IReadOnlyList<FailureDto> ExtractFailures(Type type)
    {
        var attrs = type.GetCustomAttributes<CommandFailureAttribute>(inherit: false);

        var list = new List<FailureDto>();
        foreach (var a in attrs)
            list.Add(new FailureDto(a.Code, a.Description, a.Recovery));

        return list;
    }

    // ── Tags ────────────────────────────────────────────

    private static IReadOnlyList<string> BuildTags(CommandId id, Type type)
    {
        var tags = new List<string>(4);

        var root = id.Root;
        if (!string.IsNullOrEmpty(root)) tags.Add(root);

        var category = id.Category;
        if (!string.IsNullOrEmpty(category)) tags.Add(category.ToLowerInvariant());

        if (CapabilityHeuristics.IsReadOnly(id)) tags.Add("readonly");
        if (CapabilityHeuristics.IsIdempotent(id)) tags.Add("idempotent");

        var risk = type.GetCustomAttribute<CommandRiskAttribute>();
        if (risk is not null)
        {
            tags.Add("risk:"+risk.Risk.ToString().ToLowerInvariant());
            if(risk.RequiresHumanApproval)tags.Add("approval-required");
            if(!string.IsNullOrWhiteSpace(risk.Permission))tags.Add("permission:"+risk.Permission);
        }
        var ns = type.Namespace;
        if (!string.IsNullOrEmpty(ns))
        {
            var lastSegment = ns.Split('.').LastOrDefault();
            if (!string.IsNullOrEmpty(lastSegment) &&
                !tags.Contains(lastSegment, StringComparer.OrdinalIgnoreCase))
            {
                tags.Add(lastSegment.ToLowerInvariant());
            }
        }

        return tags;
    }

    // ── Helpers ─────────────────────────────────────────

    private static string Humanize(string id)
    {
        var parts = id.Split('.');
        var sb = new StringBuilder(id.Length + 4);

        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0) continue;
            if (i > 0) sb.Append(' ');

            sb.Append(char.ToUpperInvariant(parts[i][0]));
            if (parts[i].Length > 1)
                sb.Append(parts[i][1..]);
        }

        return sb.ToString();
    }

    private static bool IsSystemAssembly(Assembly assembly)
    {
        var name = assembly.GetName().Name ?? "";

        if (name.StartsWith("System.", StringComparison.Ordinal)) return true;
        if (name.StartsWith("Microsoft.", StringComparison.Ordinal)) return true;
        if (name.StartsWith("netstandard", StringComparison.Ordinal)) return true;
        if (name == "mscorlib") return true;
        if (name == "xunit.core") return true;
        if (name == "xunit.execution.dotnet") return true;

        return false;
    }
}