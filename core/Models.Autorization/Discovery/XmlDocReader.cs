using System.Xml.Linq;

namespace Models.Autorization.Discovery
{
    internal sealed class XmlDocReader
    {
        private readonly Dictionary<string, string> _summaries;

        public XmlDocReader(string xmlPath)
        {
            _summaries = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
            catch { /* нет XML — работаем без docs */ }
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
            if (string.IsNullOrEmpty(raw)) return "";

            var lines = raw
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim())
                .Where(l => l.Length > 0);

            return string.Join(" ", lines);
        }
    }
}