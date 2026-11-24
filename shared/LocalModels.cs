using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace MysticEgo.Deployment;

internal static class LocalModels
{
    internal const string Address = "http://127.0.0.1:1234";
    internal const string Alias = "local-model";
    internal const string SmallModel = "qwen/qwen3-1.7b";
    internal const string MediumModel = "qwen/qwen3-4b";
    internal const string LargeModel = "qwen/qwen3-8b";
    internal const string LargeAlternative = "qwen/qwen3.5-9b";

    internal sealed record Installed(string Key, string[] Instances);
    internal sealed record Active(string Id, string Key);

    internal static string[] Preference(string role) => role.ToLowerInvariant() switch
    {
        "orchestra" or "global" => [LargeModel, LargeAlternative, MediumModel, SmallModel],
        "local" => [MediumModel, SmallModel],
        _ => [SmallModel]
    };

    internal static bool Matches(string key, string name)
    {
        var shortName = name[(name.LastIndexOf('/') + 1)..];
        return key.Split('/').Any(part => part.Equals(shortName, StringComparison.OrdinalIgnoreCase) ||
            part.Equals(shortName + "-gguf", StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<Installed[]> Catalog(HttpClient client, CancellationToken ct)
    {
        if (await Read(client, "/api/v1/models", ct) is { ValueKind: JsonValueKind.Object } native &&
            native.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
        {
            return models.EnumerateArray()
                .Where(model => Text(model, "type") == "llm" && Text(model, "key") is not null)
                .Select(model => new Installed(Text(model, "key")!,
                    model.TryGetProperty("loaded_instances", out var instances) && instances.ValueKind == JsonValueKind.Array ?
                        instances.EnumerateArray().Select(instance => Text(instance, "id")).OfType<string>().ToArray() : []))
                .ToArray();
        }

        if (await Read(client, "/api/v0/models", ct) is { ValueKind: JsonValueKind.Object } previous &&
            previous.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array &&
            (data.GetArrayLength() == 0 || data.EnumerateArray().Any(model => Text(model, "type") is not null)))
        {
            return data.EnumerateArray()
                .Where(model => (Text(model, "type") is "llm" or "vlm") && Text(model, "id") is not null)
                .Select(model => new Installed(Text(model, "id")!, Text(model, "state") == "loaded" ? [Text(model, "id")!] : []))
                .ToArray();
        }

        var list = await Read(client, "/v1/models", ct) ?? throw new IOException("LM Studio не предоставляет каталог моделей.");
        // The compatibility endpoint can list downloaded models while they are unloaded.
        return list.GetProperty("data").EnumerateArray().Select(model => Text(model, "id")).OfType<string>()
            .Where(id => !id.Contains("embed", StringComparison.OrdinalIgnoreCase))
            .Select(id => new Installed(id, id == Alias ? [id] : [])).ToArray();
    }

    internal static async Task<Active?> Ready(HttpClient client, string role, string? verifiedFile, CancellationToken ct)
    {
        string? verified = null;
        if (!string.IsNullOrEmpty(verifiedFile))
        {
            if (!File.Exists(verifiedFile)) return null;
            verified = (await File.ReadAllTextAsync(verifiedFile, ct)).Trim();
        }
        var models = await Catalog(client, ct);
        if (verified is not null)
        {
            var model = models.FirstOrDefault(entry => entry.Instances.Contains(verified));
            return model is null ? null : new(verified, model.Key);
        }
        foreach (var candidate in Preference(role))
        {
            var match = models.FirstOrDefault(model => model.Instances.Length > 0 && Matches(model.Key, candidate));
            if (match is not null) return new(match.Instances[0], match.Key);
        }
        var alias = models.FirstOrDefault(model => model.Instances.Contains(Alias));
        if (alias is not null) return new(Alias, alias.Key);
        var other = models.FirstOrDefault(model => model.Instances.Length > 0);
        return other is null ? null : new(other.Instances[0], other.Key);
    }

    internal static bool Unsupported(HttpStatusCode status) => status is HttpStatusCode.NotFound or
        HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented;

    internal static string? Text(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element) &&
        element.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(element.GetString()) ? element.GetString() : null;

    private static async Task<JsonElement?> Read(HttpClient client, string route, CancellationToken ct)
    {
        using var response = await client.GetAsync(Address + route, ct);
        if (Unsupported(response.StatusCode)) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>(ct);
    }
}
