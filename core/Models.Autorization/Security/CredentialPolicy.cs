using System.Text;
using System.Text.RegularExpressions;
using Models.Autorization.Configuration;

namespace Models.Autorization.Security;

/// <summary>Shared policy for account creation and password changes. Existing logins are not renamed.</summary>
public static partial class CredentialPolicy
{
    public const int MinimumPasswordLength = 15;
    public const int MaximumPasswordLength = 128;
    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]{2,38}[A-Za-z0-9]\z", RegexOptions.CultureInvariant)]
    private static partial Regex LoginPattern();
    private static readonly HashSet<string> Weak = new(StringComparer.OrdinalIgnoreCase)
    {
        "password123456789", "123456789012345", "1234567890123456",
        "qwertyuiopasdfgh", "qwertyuiopasdfghjkl", "abcdefghijklmnop",
        "passwordpassword", "administrator123", "letmeinletmein123"
    };

    /// <summary>New login: 4–40 ASCII characters, alphanumeric at both ends.</summary>
    public static bool ValidLogin(string? login) => login is not null && LoginPattern().IsMatch(login);

    /// <summary>Expose policy metadata, not credentials. API and MCP use the same source.</summary>
    public static object Describe(PasswordPolicy policy) => new
    {
        login = new { minLength = 4, maxLength = 40, characters = "ASCII letters, digits, dot, underscore, hyphen", alphanumericEnds = true, caseSensitive = true },
        password = new { minLength = Math.Max(MinimumPasswordLength,policy.MinLength), maxLength = Math.Min(MaximumPasswordLength,policy.MaxLength), unicode = true, spaces = true, policy.RequireDigit, policy.RequireLowercase, policy.RequireUppercase, policy.RequireNonAlphanumeric }
    };

    /// <summary>Returns a stable validation code, or null. Never logs or modifies the password.</summary>
    public static string? PasswordError(string? password, string? login, PasswordPolicy policy)
    {
        if (password is null || password.Length > MaximumPasswordLength * 2) return "password_length";
        var count = 0;
        foreach (var rune in password.EnumerateRunes())
        {
            if (Rune.IsControl(rune) || rune.Value == 0xFFFD) return "password_characters";
            count++;
        }
        if (count < Math.Max(MinimumPasswordLength,policy.MinLength) ||
            count > Math.Min(MaximumPasswordLength,policy.MaxLength)) return "password_length";
        if (string.IsNullOrWhiteSpace(password) || Weak.Contains(password) ||
            password.Distinct().Count() == 1 ||
            (!string.IsNullOrEmpty(login) && string.Equals(password,login,StringComparison.OrdinalIgnoreCase)))
            return "password_weak";
        // Preserve explicitly configured legacy composition requirements. Defaults do not require them.
        if (policy.RequireDigit && !password.Any(char.IsDigit) ||
            policy.RequireLowercase && !password.Any(char.IsLower) ||
            policy.RequireUppercase && !password.Any(char.IsUpper) ||
            policy.RequireNonAlphanumeric && password.All(char.IsLetterOrDigit)) return "password_composition";
        return null;
    }
}
