using System.Diagnostics;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ActionConstraints;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Models.Autorization.Attributes;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Data;
using Models.Autorization.Domain.Entities;
using Models.Commands;

namespace Models.Autorization.Discovery;

// ════════════════════════════════════════════════════════════════════
//  ToolDiscoveryService
//
//  Что делает:
//    • Сканирует все контроллеры с [ApiController].
//    • Пропускает классы с [SkipPermission] — полностью, включая методы.
//    • Пропускает методы с [SkipTool] — только метод.
//    • Создаёт Permission из имени контроллера.
//    • Создаёт Tool для каждого action.
//    • IsPublic = [AllowAnonymous] на методе или классе.
//
//  Метаданные:
//    • Permission.Code        = PermissionId.FromController(ClassName)
//    • Permission.DisplayName = XML-док класса / Name
//    • Permission.Category    = имя контроллера без "Controller"
//    • Tool.Code              = ToolId.From(PermissionId, ActionName)
//    • Tool.DisplayName       = XML-док метода / ActionName
//    • Tool.Description       = XML-док метода
//    • Tool.PathTemplate      = AttributeRouteInfo.Template
//    • Tool.HttpMethod        = HTTP-метод из ActionConstraints
// ════════════════════════════════════════════════════════════════════

public sealed class ToolDiscoveryService : IToolDiscoveryService
{
    private readonly IActionDescriptorCollectionProvider _actions;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ToolDiscoveryService> _log;

    public ToolDiscoveryService(
        IActionDescriptorCollectionProvider actions,
        IServiceScopeFactory scopeFactory,
        ILogger<ToolDiscoveryService> log)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public async Task<DiscoverySyncResult> SyncAsync(CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var result = new DiscoverySyncResult();

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        var now = DateTime.UtcNow;

        // 1. Собираем все action descriptors
        var allActions = _actions.ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>()
            .ToList();

        _log.LogInformation(
            "Discovery: получено {Count} actions из {Controllers} контроллеров",
            allActions.Count,
            allActions.Select(a => a.ControllerName).Distinct().Count());

        // 2. Группируем по контроллеру и фильтруем
        var controllers = new List<ControllerBucket>();

        foreach (var group in allActions.GroupBy(a => a.ControllerName))
        {
            var first = group.First();
            var controllerType = first.ControllerTypeInfo.AsType();

            // Маркер системы прав: [ApiController]
            var isApiController = controllerType
                .GetCustomAttribute<ApiControllerAttribute>(inherit: true) is not null;

            if (!isApiController)
            {
                _log.LogDebug(
                    "Пропускаю {Controller}: не помечен [ApiController]",
                    group.Key);
                continue;
            }

            // Skip всего контроллера
            if (controllerType.GetCustomAttribute<SkipPermissionAttribute>(inherit: false) is not null)
            {
                _log.LogInformation(
                    "Пропускаю {Controller}: [SkipPermission]",
                    group.Key);
                continue;
            }

            // Пропускаем методы с [SkipTool]
            var actions = group
                .Where(a => a.MethodInfo.GetCustomAttribute<SkipToolAttribute>(inherit: false) is null)
                .ToList();

            if (actions.Count == 0)
            {
                _log.LogDebug(
                    "Пропускаю {Controller}: все методы помечены [SkipTool]",
                    group.Key);
                continue;
            }

            var skippedMethods = group.Count() - actions.Count;
            if (skippedMethods > 0)
            {
                _log.LogDebug(
                    "{Controller}: пропущено {Count} методов с [SkipTool]",
                    group.Key, skippedMethods);
            }

            controllers.Add(new ControllerBucket(controllerType, actions));
        }

        // 3. Загружаем XML-доки всех сборок контроллеров
        var xmlReaders = BuildXmlReaders(controllers);

        // 4. Текущее состояние БД
        var existingPermissions = await db.Permissions
            .Include(p => p.Tools)
            .ToDictionaryAsync(p => p.Code, StringComparer.OrdinalIgnoreCase, ct);

        var seenPermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 5. Upsert
        foreach (var controller in controllers)
        {
            await ProcessControllerAsync(
                db, controller, xmlReaders,
                existingPermissions, seenPermissions, seenTools,
                result, now, ct);
        }

        await db.SaveChangesAsync(ct);

        // 6. Удаляем исчезнувшие Tools
        var staleTools = await db.Tools
            .Where(t => !seenTools.Contains(t.Code))
            .ToListAsync(ct);

        if (staleTools.Count > 0)
        {
            _log.LogInformation(
                "Удаляю {Count} устаревших Tools", staleTools.Count);

            db.Tools.RemoveRange(staleTools);
            result.ToolsRemoved = staleTools.Count;
            await db.SaveChangesAsync(ct);
        }

        // 7. Удаляем исчезнувшие Permissions (только не системные и без tools)
        var stalePerms = await db.Permissions
            .Include(p => p.Tools)
            .Where(p => !p.IsSystem)
            .ToListAsync(ct);

        var toRemove = stalePerms
            .Where(p => !seenPermissions.Contains(p.Code) && p.Tools.Count == 0)
            .ToList();

        if (toRemove.Count > 0)
        {
            _log.LogInformation(
                "Удаляю {Count} устаревших Permissions", toRemove.Count);

            db.Permissions.RemoveRange(toRemove);
            await db.SaveChangesAsync(ct);
        }

        sw.Stop();
        result.ElapsedMs = sw.ElapsedMilliseconds;

        _log.LogInformation(
            "Discovery завершён за {Ms}мс: " +
            "Permissions +{PC}/~{PU}, Tools +{TC}/~{TU}/-{TR}",
            result.ElapsedMs,
            result.PermissionsCreated, result.PermissionsUpdated,
            result.ToolsCreated, result.ToolsUpdated, result.ToolsRemoved);

        return result;
    }

    // ── ProcessControllerAsync ────────────────────────────

    private async Task ProcessControllerAsync(
        AuthDbContext db,
        ControllerBucket bucket,
        IReadOnlyDictionary<Assembly, XmlDocReader> xmlReaders,
        Dictionary<string, Permission> existingPermissions,
        HashSet<string> seenPermissions,
        HashSet<string> seenTools,
        DiscoverySyncResult result,
        DateTime now,
        CancellationToken ct)
    {
        var controllerType = bucket.ControllerType;
        var actions = bucket.Actions;

        var permissionId = PermissionId.FromController(controllerType.Name);
        var permCode = permissionId.Value;

        seenPermissions.Add(permCode);

        // XML-док класса
        var xml = xmlReaders.GetValueOrDefault(controllerType.Assembly);
        var typeSummary = xml?.GetTypeSummary(controllerType.FullName ?? controllerType.Name);

        var permDisplayName = !string.IsNullOrWhiteSpace(typeSummary)
            ? typeSummary
            : HumanizeControllerName(controllerType.Name);

        var permCategory = DeriveCategory(controllerType.Name);

        // Upsert Permission
        if (!existingPermissions.TryGetValue(permCode, out var permission))
        {
            permission = new Permission
            {
                Code = permCode,
                DisplayName = permDisplayName,
                Description = typeSummary,
                Category = permCategory,
                IsSystem = false,
                ControllerName = controllerType.Name,
                DiscoveredAt = now,
                UpdatedAt = now,
            };

            db.Permissions.Add(permission);
            await db.SaveChangesAsync(ct);

            existingPermissions[permCode] = permission;
            result.PermissionsCreated++;

            _log.LogInformation(
                "  + Permission '{Code}' ({Display})",
                permCode, permDisplayName);
        }
        else
        {
            var dirty = false;

            if (permission.DisplayName != permDisplayName)
            {
                permission.DisplayName = permDisplayName;
                dirty = true;
            }

            if (permission.Description != typeSummary)
            {
                permission.Description = typeSummary;
                dirty = true;
            }

            if (permission.Category != permCategory)
            {
                permission.Category = permCategory;
                dirty = true;
            }

            if (permission.ControllerName != controllerType.Name)
            {
                permission.ControllerName = controllerType.Name;
                dirty = true;
            }

            if (dirty)
            {
                permission.UpdatedAt = now;
                result.PermissionsUpdated++;
            }
        }

        // Upsert Tools
        foreach (var action in actions)
        {
            var toolId = ToolId.From(permissionId, action.ActionName);
            var toolCode = toolId.Value;

            seenTools.Add(toolCode);

            var methodSummary = xml?.GetMethodSummary(
                controllerType.FullName ?? controllerType.Name,
                action.ActionName);

            var toolDisplayName = !string.IsNullOrWhiteSpace(methodSummary)
                ? methodSummary
                : HumanizeActionName(action.ActionName);

            var httpMethod = GetHttpMethod(action);
            var pathTemplate = action.AttributeRouteInfo?.Template?.Trim('/').ToLowerInvariant() ?? "";

            // IsPublic = [AllowAnonymous] на методе или контроллере
            var isPublic =
                action.MethodInfo.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null
                || controllerType.GetCustomAttribute<AllowAnonymousAttribute>(inherit: true) is not null;

            var existingTool = permission.Tools
                .FirstOrDefault(t => string.Equals(t.Code, toolCode, StringComparison.OrdinalIgnoreCase));

            if (existingTool is null)
            {
                db.Tools.Add(new Tool
                {
                    Code = toolCode,
                    DisplayName = toolDisplayName,
                    Description = methodSummary,
                    HttpMethod = httpMethod,
                    PathTemplate = pathTemplate,
                    PermissionId = permission.Id,
                    IsEnabled = true,
                    IsPublic = isPublic,
                    DiscoveredAt = now,
                    UpdatedAt = now,
                });

                result.ToolsCreated++;

                _log.LogDebug(
                    "    + Tool '{Code}' ({Method} {Path}) public={Public}",
                    toolCode, httpMethod, pathTemplate, isPublic);
            }
            else
            {
                var dirty = false;

                if (existingTool.DisplayName != toolDisplayName) { existingTool.DisplayName = toolDisplayName; dirty = true; }
                if (existingTool.Description != methodSummary) { existingTool.Description = methodSummary; dirty = true; }
                if (existingTool.HttpMethod != httpMethod) { existingTool.HttpMethod = httpMethod; dirty = true; }
                if (existingTool.PathTemplate != pathTemplate) { existingTool.PathTemplate = pathTemplate; dirty = true; }
                if (existingTool.IsPublic != isPublic) { existingTool.IsPublic = isPublic; dirty = true; }

                if (dirty)
                {
                    existingTool.UpdatedAt = now;
                    result.ToolsUpdated++;
                }
            }
        }

        await db.SaveChangesAsync(ct);
    }

    // ── Helpers ───────────────────────────────────────────

    private static IReadOnlyDictionary<Assembly, XmlDocReader> BuildXmlReaders(
        IReadOnlyList<ControllerBucket> controllers)
    {
        var readers = new Dictionary<Assembly, XmlDocReader>();
        var assemblies = controllers
            .Select(c => c.ControllerType.Assembly)
            .Distinct();

        foreach (var asm in assemblies)
        {
            var xmlPath = Path.Combine(
                AppContext.BaseDirectory,
                asm.GetName().Name + ".xml");

            readers[asm] = new XmlDocReader(xmlPath);
        }

        return readers;
    }

    private static string DeriveCategory(string controllerName)
    {
        // "AuthController" → "Auth"
        var name = controllerName;
        if (name.EndsWith("Controller", StringComparison.Ordinal))
            name = name[..^"Controller".Length];

        // Одно слово: "Auth" → "Auth"
        // Несколько: "UsersRoles" → "Users"
        var firstUpper = name.TakeWhile(c => !char.IsUpper(c) || c == name[0]).Count();
        return firstUpper > 0 ? name[..firstUpper] : name;
    }

    private static string HumanizeControllerName(string controllerName)
    {
        var name = controllerName;
        if (name.EndsWith("Controller", StringComparison.Ordinal))
            name = name[..^"Controller".Length];

        return Humanize(name);
    }

    private static string HumanizeActionName(string actionName)
        => Humanize(actionName);

    private static string Humanize(string pascal)
    {
        var sb = new System.Text.StringBuilder(pascal.Length + 4);

        for (var i = 0; i < pascal.Length; i++)
        {
            var ch = pascal[i];

            if (i > 0 && char.IsUpper(ch))
                sb.Append(' ');

            sb.Append(ch);
        }

        return sb.ToString();
    }

    private static string GetHttpMethod(ControllerActionDescriptor d)
    {
        var m = d.ActionConstraints?
            .OfType<HttpMethodActionConstraint>()
            .SelectMany(x => x.HttpMethods)
            .FirstOrDefault();

        return (m ?? "GET").ToUpperInvariant();
    }

    // ── Internal types ────────────────────────────────────

    private sealed record ControllerBucket(
        Type ControllerType,
        IReadOnlyList<ControllerActionDescriptor> Actions);

    // ── XmlDocReader ──────────────────────────────────────

    private sealed class XmlDocReader
    {
        private readonly Dictionary<string, string> _summaries =
            new(StringComparer.OrdinalIgnoreCase);

        public XmlDocReader(string xmlPath)
        {
            if (!File.Exists(xmlPath)) return;

            try
            {
                var doc = XDocument.Load(xmlPath);

                foreach (var member in doc.Descendants("member"))
                {
                    var name = member.Attribute("name")?.Value;
                    if (string.IsNullOrEmpty(name)) continue;

                    var summary = member.Element("summary");
                    if (summary is null) continue;

                    var text = NormalizeText(summary.Value);
                    if (string.IsNullOrEmpty(text)) continue;

                    _summaries[name] = text;
                }
            }
            catch
            {
                // Битый XML — работаем без описаний
            }
        }

        public string? GetTypeSummary(string typeFullName)
        {
            var key = $"T:{typeFullName}";
            return _summaries.TryGetValue(key, out var v) ? v : null;
        }

        public string? GetMethodSummary(string typeFullName, string methodName)
        {
            var prefix = $"M:{typeFullName}.{methodName}";

            foreach (var (key, value) in _summaries)
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return value;

            return null;
        }

        private static string NormalizeText(string raw)
        {
            var lines = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);

            return string.Join(" ", lines);
        }
    }
}