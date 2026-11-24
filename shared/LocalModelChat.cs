using System.Net;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MysticEgo.Deployment;

public sealed record LocalModelMessage(string Role, string Content);
public sealed record LocalModelReply(string Text, JsonElement Tools);

internal static class LocalModelChat
{
    private static readonly JsonSerializerOptions HistoryEncoding = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    internal static async Task<LocalModelReply> Respond(HttpClient http, string model, string system,
        IReadOnlyList<LocalModelMessage> messages, int tokens, double temperature, object? tools, CancellationToken ct, string? family = null)
    {
        var qwen35 = (family ?? model).Contains("qwen3.5", StringComparison.OrdinalIgnoreCase);
        if (qwen35 && tools is null && await Direct(http, model, system, messages, tokens, temperature, ct) is { } direct)
            return new(direct, default);

        var body = new Dictionary<string, object>
        {
            ["model"] = model,
            ["messages"] = new[] { new LocalModelMessage("system", system) }.Concat(messages),
            ["max_tokens"] = qwen35 ? Math.Max(2048, tokens) : tokens,
            ["temperature"] = temperature,
            ["stream"] = false
        };
        if (qwen35) body["chat_template_kwargs"] = new { enable_thinking = false };
        if (tools is not null) body["tools"] = tools;
        using var response = await http.PostAsJsonAsync(LocalModels.Address + "/v1/chat/completions", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            if (qwen35 && tools is not null && (int)response.StatusCode >= 400 &&
                response.StatusCode is not (HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) &&
                await Direct(http, model, system, messages, tokens, temperature, ct) is { } recovered)
                return new(recovered, default);
            throw await Error(response, ct);
        }

        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!result.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0 ||
            !choices[0].TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object)
            throw new IOException("LM Studio не вернула сообщение.");
        var answer = FinalText(LocalModels.Text(message, "content"));
        if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
            return new(answer, calls);
        if (answer.Length > 0) return new(answer, default);
        if (qwen35 && tools is not null && await Direct(http, model, system, messages, tokens, temperature, ct) is { } fallback)
            return new(fallback, default);
        throw new IOException("LM Studio не вернула итоговый текст. Модель могла исчерпать лимит на рассуждения.");
    }

    private static async Task<string?> Direct(HttpClient http, string model, string system, IReadOnlyList<LocalModelMessage> messages,
        int tokens, double temperature, CancellationToken ct)
    {
        var latest = messages[^1].Content;
        if (messages.Count > 1)
            latest = "Предыдущие реплики (JSON):\n" + JsonSerializer.Serialize(messages.Take(messages.Count - 1), HistoryEncoding) +
                "\n\nТекущее сообщение пользователя:\n" + latest;
        using var response = await http.PostAsJsonAsync(LocalModels.Address + "/api/v1/chat", new
        {
            model, input = latest, system_prompt = system, reasoning = "off", max_output_tokens = tokens, temperature, stream = false, store = false
        }, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw await Error(response, ct);
        if (!response.IsSuccessStatusCode) return null;
        var result = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!result.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return null;
        var text = string.Join('\n', output.EnumerateArray().Where(item => LocalModels.Text(item, "type") == "message")
            .Select(item => FinalText(LocalModels.Text(item, "content"))).Where(part => part.Length > 0));
        return text.Length == 0 ? null : text;
    }

    private static string FinalText(string? input)
    {
        var value = input?.Trim() ?? "";
        var close = value.LastIndexOf("</think>", StringComparison.OrdinalIgnoreCase);
        if (close >= 0) value = value[(close + "</think>".Length)..].Trim();
        return value.StartsWith("<think>", StringComparison.OrdinalIgnoreCase) ? "" : value;
    }

    internal static async Task<IOException> Error(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var json = JsonDocument.Parse(body);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("error", out var error))
                body = LocalModels.Text(error, "message") ?? (error.ValueKind == JsonValueKind.String ? error.GetString()! : body);
        }
        catch (JsonException) { }
        body = body.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return new IOException($"LM Studio, HTTP {(int)response.StatusCode}. {body[..Math.Min(body.Length, 300)]}");
    }
}
