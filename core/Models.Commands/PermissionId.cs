using System.Diagnostics;

namespace Models.Commands
{
    /// <summary>
    /// Идентификатор права = имя контроллера без "Controller" в lower.dot.case.
    ///
    ///   RolesController         → "roles"
    ///   AudioDevicesController  → "audio.devices"
    ///
    /// Один PermissionId объединяет N ToolId.
    /// </summary>
    [DebuggerDisplay("{Value}")]
    public readonly struct PermissionId : IEquatable<PermissionId>, IComparable<PermissionId>
    {
        private readonly string? _value;

        public string Value => _value ?? string.Empty;

        public bool IsEmpty => string.IsNullOrEmpty(_value);

        private PermissionId(string value) => _value = value;

        /// <summary>Из имени контроллера (с суффиксом "Controller").</summary>
        public static PermissionId FromController(string controllerName)
            => new(IdentifierConvention.FromTypeName(controllerName, "Controller"));

        /// <summary>Из типа контроллера.</summary>
        public static PermissionId FromController(Type controllerType)
        {
            ArgumentNullException.ThrowIfNull(controllerType);
            return FromController(controllerType.Name);
        }

        /// <summary>Из явного имени.</summary>
        public static PermissionId FromName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new IdentifierException("Не указано имя разрешения PermissionId.");

            var normalized = IdentifierConvention.Normalize(name);

            if (string.IsNullOrEmpty(normalized))
                throw new IdentifierException(
                    $"PermissionId name is invalid after normalization: '{name}'.");

            return new(normalized);
        }

        public static bool TryParse(string? name, out PermissionId id)
        {
            id = default;

            if (!IdentifierConvention.TryNormalize(name, out var normalized)) return false;

            id = new(normalized);
            return true;
        }

        public string Category => IdentifierConvention.CategoryFromId(Value);

        // ── Equality ───────────────────────────────────────

        public bool Equals(PermissionId other)
            => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is PermissionId other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode(StringComparison.Ordinal);

        public int CompareTo(PermissionId other)
            => string.Compare(Value, other.Value, StringComparison.Ordinal);

        public override string ToString() => Value;

        public static bool operator ==(PermissionId a, PermissionId b) => a.Equals(b);
        public static bool operator !=(PermissionId a, PermissionId b) => !a.Equals(b);

        public static implicit operator string(PermissionId id) => id.Value;
    }
}