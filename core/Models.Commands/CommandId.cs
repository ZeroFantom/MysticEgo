using System.Diagnostics;
using System.Windows.Input;

namespace Models.Commands
{
    /// <summary>
    /// Идентификатор команды клиента. На сервере тот же Value — это ToolId.
    ///
    /// Гарантии:
    ///   • lower.dot.case
    ///   • не пустой для значений, созданных через From / FromName / TryParse
    ///   • Value для default(CommandId) == ""
    /// </summary>
    [DebuggerDisplay("{Value}")]
    public readonly struct CommandId : IEquatable<CommandId>, IComparable<CommandId>
    {
        private readonly string? _value;

        public string Value => _value ?? string.Empty;

        public bool IsEmpty => string.IsNullOrEmpty(_value);

        private CommandId(string value) => _value = value;

        /// <summary>
        /// Из типа команды. Имя класса → идентификатор.
        ///   typeof(AudioListCommand) → "audio.list"
        /// </summary>
        public static CommandId From<T>() where T : ICommand
            => new(IdentifierConvention.FromTypeName(typeof(T).Name, "Command"));

        /// <summary>Из явного имени. Нормализует формат.</summary>
        public static CommandId FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new IdentifierException("Не указано имя команды CommandId.");

            var normalized = IdentifierConvention.Normalize(name);

            if (string.IsNullOrEmpty(normalized))
                throw new IdentifierException(
                    $"CommandId name is invalid after normalization: '{name}'.");

            return new(normalized);
        }

        /// <summary>Попытка разобрать. Не бросает.</summary>
        public static bool TryParse(string? name, out CommandId id)
        {
            id = default;

            if (!IdentifierConvention.TryNormalize(name, out var normalized)) return false;

            id = new(normalized);
            return true;
        }

        /// <summary>Категория (первый сегмент в PascalCase). "audio.list" → "Audio".</summary>
        public string Category => IdentifierConvention.CategoryFromId(Value);

        /// <summary>Первое слово. "audio.list" → "audio".</summary>
        public string Root => IdentifierConvention.RootFromId(Value);

        /// <summary>Последнее слово. "audio.list" → "list".</summary>
        public string Leaf => IdentifierConvention.LeafFromId(Value);

        // ── Equality ───────────────────────────────────────

        public bool Equals(CommandId other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is CommandId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

        public int CompareTo(CommandId other)
            => string.Compare(Value, other.Value, StringComparison.Ordinal);

        public override string ToString() => Value;

        public static bool operator ==(CommandId a, CommandId b) => a.Equals(b);
        public static bool operator !=(CommandId a, CommandId b) => !a.Equals(b);

        public static implicit operator string(CommandId id) => id.Value;
    }
}