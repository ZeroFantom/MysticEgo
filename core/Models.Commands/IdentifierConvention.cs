using System.Text;

namespace Models.Commands
{
    /// <summary>
    /// Единое правило: PascalCase → lower.dot.case.
    ///
    /// Разбивает по границам «строчная → прописная» и «аббревиатура → слово».
    /// Числа остаются приклеенными к предыдущему слову.
    ///
    ///   AudioListCommand          → audio.list
    ///   RgbMouseCommand           → rgb.mouse
    ///   RGBCommand                → rgb
    ///   AudioRGBCommand           → audio.rgb
    ///   AudioRGBListCommand       → audio.rgb.list
    ///   Http2ServerCommand        → http2.server
    ///   XMLHttpRequestCommand     → xml.http.request
    ///
    /// Гарантии:
    ///   • никогда не возвращает null
    ///   • никогда не содержит двойных точек
    ///   • никогда не начинается и не заканчивается точкой
    ///   • идемпотентно: Normalize(Normalize(x)) == Normalize(x)
    /// </summary>
    public static class IdentifierConvention
    {
        /// <summary>
        /// Имя типа (с суффиксом) → идентификатор.
        /// Суффикс отрезается, если присутствует.
        /// Generic-арность (Foo`1) отрезается всегда.
        /// </summary>
        public static string FromTypeName(string typeName, string? suffix = null)
        {
            if (string.IsNullOrEmpty(typeName))
                throw new ArgumentException("Необходимо указать имя типа (typeName).", nameof(typeName));

            // Foo`1 → Foo
            var backtick = typeName.IndexOf('`');
            if (backtick >= 0)
                typeName = typeName[..backtick];

            // AudioListCommand → AudioList (если суффикс совпал)
            if (!string.IsNullOrEmpty(suffix) &&
                typeName.EndsWith(suffix, StringComparison.Ordinal) &&
                typeName.Length > suffix.Length)
            {
                typeName = typeName[..^suffix.Length];
            }

            return Normalize(typeName);
        }

        /// <summary>
        /// Произвольная строка → нормализованный идентификатор.
        /// Пробелы, дефисы, подчёркивания, слэши превращаются в точки.
        /// </summary>
        public static string Normalize(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            var sb = new StringBuilder(name.Length + 4);

            for (var i = 0; i < name.Length; i++)
            {
                var ch = name[i];

                // Явные разделители → точка (без дублей)
                if (char.IsWhiteSpace(ch) || ch is '.' or '-' or '_' or '/' or '\\')
                {
                    AppendSeparator(sb);
                    continue;
                }

                if (char.IsUpper(ch))
                {
                    var prev = i > 0 ? name[i - 1] : '\0';
                    var next = i + 1 < name.Length ? name[i + 1] : '\0';

                    var prevIsLowerOrDigit = char.IsLower(prev) || char.IsDigit(prev);
                    var endOfAcronym = char.IsUpper(prev) && char.IsLower(next);

                    if (prevIsLowerOrDigit || endOfAcronym)
                        AppendSeparator(sb);

                    sb.Append(char.ToLowerInvariant(ch));
                }
                else
                {
                    sb.Append(ch);
                }
            }

            TrimTrailingSeparator(sb);
            return sb.ToString();
        }

        /// <summary>
        /// Категория — первый сегмент идентификатора в PascalCase.
        ///   audio.list    → Audio
        ///   rgb.mouse     → Rgb
        ///   roles         → Roles
        ///   ""            → ""
        /// </summary>
        public static string CategoryFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;

            var first = RootFromId(id);
            if (first.Length == 0) return string.Empty;

            return char.ToUpperInvariant(first[0]) + first[1..];
        }

        /// <summary>
        /// Первое слово идентификатора (до первой точки).
        /// </summary>
        public static string RootFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var dot = id.IndexOf('.');
            return dot < 0 ? id : id[..dot];
        }

        /// <summary>
        /// Последнее слово идентификатора (после последней точки).
        /// </summary>
        public static string LeafFromId(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var dot = id.LastIndexOf('.');
            return dot < 0 ? id : id[(dot + 1)..];
        }

        internal static bool TryNormalize(string? name, out string value, bool qualified = false)
        {
            value = name is null ? string.Empty : Normalize(name);
            return value.Length > 0 && (!qualified || value.Contains('.'));
        }

        // ── Private ────────────────────────────────────────

        private static void AppendSeparator(StringBuilder sb)
        {
            if (sb.Length == 0) return;
            if (sb[^1] == '.') return;
            sb.Append('.');
        }

        private static void TrimTrailingSeparator(StringBuilder sb)
        {
            while (sb.Length > 0 && sb[^1] == '.')
                sb.Length--;
        }
    }
}