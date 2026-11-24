using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Models.Commands.SignalR;

namespace Models.Commands.Mcp;

public sealed class McpConverter : IMcpConverter
{
    private readonly ILogger<McpConverter> _log;

    public McpConverter(ILogger<McpConverter>? log = null)
        => _log = log ?? NullLogger<McpConverter>.Instance;

    public McpToolDescriptor ToTool(CapabilityDto capability)
    {
        if (capability is null)
            throw new ArgumentNullException(nameof(capability));

        return new McpToolDescriptor
        {
            Name = capability.Id.Value,
            Description = BuildDescription(capability),
            InputSchema = BuildInputSchema(capability.Parameters),
            Annotations = new McpAnnotations
            {
                Title = string.IsNullOrWhiteSpace(capability.DisplayName)
                    ? capability.Id.Value
                    : capability.DisplayName,
                ReadOnlyHint = capability.Tags.Contains("readonly", StringComparer.OrdinalIgnoreCase),
                IdempotentHint = capability.Tags.Contains("idempotent", StringComparer.OrdinalIgnoreCase),
                OpenWorldHint = false,
            },
            Category = string.IsNullOrWhiteSpace(capability.Category)
                ? null
                : capability.Category,
            Tags = capability.Tags.Count == 0 ? null : capability.Tags.ToList(),
        };
    }

    public IEnumerable<McpToolDescriptor> ToTools(IEnumerable<CapabilityDto> capabilities)
    {
        if (capabilities is null) yield break;

        foreach (var cap in capabilities)
        {
            McpToolDescriptor? tool = null;

            try { tool = ToTool(cap); }
            catch (Exception ex)
            {
                _log.LogWarning(ex,
                    "Не удалось сконвертировать capability '{Id}' — пропуск",
                    cap?.Id.Value ?? "?");
            }

            if (tool is not null) yield return tool;
        }
    }

    // ── Description ────────────────────────────────────

    private static string BuildDescription(CapabilityDto cap)
    {
        var lines = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(cap.Description))
            lines.Add(cap.Description);

        if (cap.Examples.Count > 0)
        {
            lines.Add("Examples:");
            foreach (var ex in cap.Examples.Take(3))
                lines.Add($"  - {ex.Invocation}   {ex.Description}");
        }

        if (cap.Failures.Count > 0)
        {
            lines.Add("Known failures:");
            foreach (var f in cap.Failures.Take(3))
                lines.Add($"  - {f.Code}: {f.Description}" +
                          (string.IsNullOrEmpty(f.Recovery) ? "" : $" → {f.Recovery}"));
        }

        return lines.Count == 0
            ? $"Command '{cap.Id.Value}'."
            : string.Join('\n', lines);
    }

    // ── Input schema ───────────────────────────────────

    private static McpInputSchema BuildInputSchema(IReadOnlyList<ParameterDto> parameters)
    {
        var schema = new McpInputSchema();

        if (parameters is null || parameters.Count == 0)
            return schema;

        foreach (var p in parameters)
        {
            var prop = new McpPropertySchema
            {
                Type = MapClrTypeToJsonSchemaType(p.ClrType),
                Description = string.IsNullOrWhiteSpace(p.Description) ? null : p.Description,
                Default = p.Default,
                Format = string.IsNullOrWhiteSpace(p.Format) ? null : p.Format,
                Enum = p.Values is { Count: > 0 } ? p.Values.ToList() : null,
            };

            schema.Properties[p.Name] = prop;

            if (p.Required)
                schema.Required.Add(p.Name);
        }

        return schema;
    }

    /// <summary>
    /// "System.String"      → "string"
    /// "System.Int32"       → "integer"
    /// "System.Double"      → "number"
    /// "System.Boolean"     → "boolean"
    /// "System.String[]"    → "array"
    /// Прочее               → "string"
    /// </summary>
    private static string MapClrTypeToJsonSchemaType(string clrType)
    {
        if (string.IsNullOrEmpty(clrType)) return "string";

        return clrType switch
        {
            "System.String" => "string",
            "System.Char" => "string",

            "System.Boolean" => "boolean",

            "System.Byte" => "integer",
            "System.SByte" => "integer",
            "System.Int16" => "integer",
            "System.UInt16" => "integer",
            "System.Int32" => "integer",
            "System.UInt32" => "integer",
            "System.Int64" => "integer",
            "System.UInt64" => "integer",

            "System.Single" => "number",
            "System.Double" => "number",
            "System.Decimal" => "number",

            _ when clrType.EndsWith("[]", StringComparison.Ordinal) => "array",

            _ => "string",
        };
    }
}