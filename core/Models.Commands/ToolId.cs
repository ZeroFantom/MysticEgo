using System.Diagnostics;

namespace Models.Commands
{
    /// <summary>
    /// Идентификатор инструмента = PermissionId + метод.
    /// Формат: {permission}.{method} — всегда две части, минимум одна точка.
    ///
    /// Контракт: ToolId.Value == CommandId.Value.
    /// Один и тот же инструмент вызывается и через API, и через клиент.
    ///
    ///   PermissionId "roles" + метод "List"   → "roles.list"
    ///   PermissionId "users" + метод "Create" → "users.create"
    /// </summary>
    [DebuggerDisplay("{Value}")]
    public readonly struct ToolId : IEquatable<ToolId>, IComparable<ToolId>
    {
        private readonly string? _value;

        public string Value => _value ?? string.Empty;

        public bool IsEmpty => string.IsNullOrEmpty(_value);

        private ToolId(string value) => _value = value;

        /// <summary>Собрать из PermissionId и имени метода.</summary>
        public static ToolId From(PermissionId permission, string methodName)
        {
            if (permission.IsEmpty)
                throw new IdentifierException(
                    "ToolId requires a non-empty PermissionId.");

            if (string.IsNullOrWhiteSpace(methodName))
                throw new IdentifierException("Необходимо указать имя метода ToolId.");

            var method = IdentifierConvention.Normalize(methodName);

            if (string.IsNullOrEmpty(method))
                throw new IdentifierException(
                    $"ToolId method name is invalid: '{methodName}'.");

            return new($"{permission.Value}.{method}");
        }

        /// <summary>
        /// Из полного имени. Требуется минимум одна точка.
        /// "roles.list" → Permission="roles", Method="list".
        /// </summary>
        public static ToolId FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new IdentifierException("Не указано имя инструмента ToolId.");

            var normalized = IdentifierConvention.Normalize(name);

            if (string.IsNullOrEmpty(normalized))
                throw new IdentifierException(
                    $"ToolId name is invalid after normalization: '{name}'.");

            if (!normalized.Contains('.'))
                throw new IdentifierException(
                    $"ToolId must have format 'permission.method': '{normalized}'.");

            return new(normalized);
        }

        public static bool TryParse(string? name, out ToolId id)
        {
            id = default;

            if (string.IsNullOrWhiteSpace(name)) return false;

            var normalized = IdentifierConvention.Normalize(name);
            if (string.IsNullOrEmpty(normalized)) return false;
            if (!normalized.Contains('.')) return false;

            id = new(normalized);
            return true;
        }

        /// <summary>Permission-часть: всё до последней точки.</summary>
        public PermissionId Permission
        {
            get
            {
                if (IsEmpty) return default;

                var lastDot = Value.LastIndexOf('.');
                if (lastDot <= 0) return default;

                return PermissionId.FromName(Value[..lastDot]);
            }
        }

        /// <summary>Method-часть: всё после последней точки.</summary>
        public string Method
        {
            get
            {
                if (IsEmpty) return string.Empty;

                var lastDot = Value.LastIndexOf('.');
                return lastDot < 0 ? Value : Value[(lastDot + 1)..];
            }
        }

        public string Category => IdentifierConvention.CategoryFromId(Value);

        /// <summary>
        /// Приводит к CommandId. Тот же Value — это контракт маршрутизации.
        /// </summary>
        public CommandId AsCommandId()
        {
            if (IsEmpty)
                throw new IdentifierException("Нельзя преобразовать пустой ToolId в CommandId.");

            return CommandId.FromName(Value);
        }

        // ── Equality ───────────────────────────────────────

        public bool Equals(ToolId other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is ToolId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

        public int CompareTo(ToolId other)
            => string.Compare(Value, other.Value, StringComparison.Ordinal);

        public override string ToString() => Value;

        public static bool operator ==(ToolId a, ToolId b) => a.Equals(b);
        public static bool operator !=(ToolId a, ToolId b) => !a.Equals(b);

        public static implicit operator string(ToolId id) => id.Value;
    }
}