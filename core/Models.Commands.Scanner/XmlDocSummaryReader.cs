using System.Reflection;
using System.Xml.Linq;

namespace Models.Commands.Scanner;

internal sealed class XmlDocSummaryReader
{
    private readonly Dictionary<string, string> _summaries =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, string> _paramDescriptions =
        new(StringComparer.OrdinalIgnoreCase);

    public static XmlDocSummaryReader ForAssembly(Assembly assembly)
    {
        var reader = new XmlDocSummaryReader();

        if (assembly is null) return reader;

        var xmlPath = Path.Combine(
            AppContext.BaseDirectory,
            assembly.GetName().Name + ".xml");

        if (!File.Exists(xmlPath)) return reader;

        try
        {
            var doc = XDocument.Load(xmlPath);
            reader.LoadFrom(doc);
        }
        catch
        {
            // Битый XML — работаем без описаний. Не падаем.
        }

        return reader;
    }

    private void LoadFrom(XDocument doc)
    {
        foreach (var member in doc.Descendants("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrEmpty(name)) continue;

            var summary = member.Element("summary")?.Value;
            if (!string.IsNullOrWhiteSpace(summary))
                _summaries[name] = NormalizeSummary(summary);

            foreach (var param in member.Elements("param"))
            {
                var paramName = param.Attribute("name")?.Value;
                var paramText = param.Value;

                if (string.IsNullOrWhiteSpace(paramName)) continue;
                if (string.IsNullOrWhiteSpace(paramText)) continue;

                var key = $"{name}|{paramName}";
                _paramDescriptions[key] = NormalizeSummary(paramText);
            }
        }
    }

    public string? GetTypeSummary(Type type)
        => _summaries.TryGetValue("T:" + type.FullName, out var v) ? v : null;

    public string? GetMethodSummary(MethodInfo method)
    {
        var prefix = "M:" + method.DeclaringType?.FullName + "." + method.Name;

        foreach (var (key, value) in _summaries)
            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value;

        return null;
    }

    public string? GetParameterDescription(MethodInfo method, string paramName)
    {
        var prefix = "M:" + method.DeclaringType?.FullName + "." + method.Name;

        foreach (var (key, value) in _paramDescriptions)
        {
            if (!key.EndsWith("|" + paramName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return value;
        }

        return null;
    }

    private static string NormalizeSummary(string raw)
    {
        var lines = raw
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0);

        return string.Join(" ", lines);
    }
}