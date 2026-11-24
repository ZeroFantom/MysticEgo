namespace Models.Commands.Scanner;

internal static class CapabilityHeuristics
{
    private static readonly HashSet<string> ReadOnlyVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "get", "list", "show", "describe", "info", "status",
            "read", "find", "search", "query", "check",
            "has", "is", "can", "count", "ping", "dump",
        };

    private static readonly HashSet<string> IdempotentVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "set", "apply", "update", "configure", "refresh",
            "reload", "sync", "reset", "clear", "ensure",
            "register", "unregister",
        };

    private static readonly HashSet<string> NonIdempotentVerbs =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "create", "add", "insert", "delete", "remove",
            "increment", "decrement", "toggle", "push", "pop",
            "send", "publish", "start", "stop", "restart",
        };

    public static bool IsReadOnly(CommandId id)
        => ReadOnlyVerbs.Contains(id.Leaf);

    public static bool IsIdempotent(CommandId id)
    {
        if (IsReadOnly(id)) return true;

        var leaf = id.Leaf;

        if (NonIdempotentVerbs.Contains(leaf)) return false;
        if (IdempotentVerbs.Contains(leaf)) return true;

        return true;
    }

    public static string? InferFormat(Type type, string? paramName)
    {
        if (type != typeof(string)) return null;

        var name = paramName?.ToLowerInvariant() ?? "";

        if (name.Contains("color") || name.Contains("colour")) return "color";
        if (name.Contains("path") || name.Contains("file") || name.Contains("dir")) return "path";
        if (name.Contains("uri") || name.Contains("url") || name.Contains("link")) return "uri";
        if (name.Contains("email") || name.Contains("mail")) return "email";
        if (name.Contains("phone") || name.Contains("tel")) return "phone";
        if (name.Contains("host") || name.Contains("domain")) return "hostname";

        return null;
    }
}