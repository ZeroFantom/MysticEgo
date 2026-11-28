using System.Globalization;
using System.Text.Json;

namespace Models.Autorization.Services;

/// <summary>
/// VK ID server-side authorization-code exchange. Uses fixed HTTPS endpoints and
/// PKCE. The caller must atomically claim the server-owned state, validate the browser
/// callback and supply the stored verifier, redirect and client ID. Never accept an
/// access token, verifier, endpoint or client ID from the browser as proof.
/// </summary>
public sealed class VkIdentityProof : IDisposable
{
    private readonly HttpClient http;
    private const string Origin = "https://id.vk.com";
    public VkIdentityProof() : this(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { }
    // The injected handler is a trusted composition/test boundary, not request data.
    public VkIdentityProof(HttpMessageHandler handler) =>
        http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 65536 };

    public async Task<ExternalIdentityKey?> Exchange(string clientId, Uri redirect, string expectedState,
        string callbackState, string verifier, string code, string deviceId, CancellationToken ct = default)
    {
        if (!TelegramIdentityProof.PositiveId(clientId) || redirect is null || !redirect.IsAbsoluteUri ||
            redirect.Scheme != Uri.UriSchemeHttps || redirect.UserInfo.Length != 0 || redirect.Fragment.Length != 0 ||
            !Bounded(expectedState, 256) || expectedState != callbackState ||
            !Pkce(verifier) || !Bounded(code, 4096) || !Bounded(deviceId, 512)) return null;
        try
        {
            using var response = await Post("/oauth2/auth", new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code", ["client_id"] = clientId,
                ["redirect_uri"] = redirect.AbsoluteUri, ["state"] = expectedState,
                ["code_verifier"] = verifier, ["code"] = code, ["device_id"] = deviceId
            }, ct);
            if (response is null || !Valid(response.RootElement) ||
                TelegramIdentityProof.Text(response.RootElement, "state") != expectedState ||
                TelegramIdentityProof.Text(response.RootElement, "access_token") is not { } access ||
                !Bounded(access, 8192) || !Subject(response.RootElement, "user_id", out var expectedUser))
                return null;
            using var info = await Post("/oauth2/user_info", new Dictionary<string, string>
            {
                ["client_id"] = clientId, ["access_token"] = access
            }, ct);
            if (info is null || !Valid(info.RootElement) ||
                !info.RootElement.TryGetProperty("user", out var user) ||
                !TelegramIdentityProof.UniqueObject(user) || !Subject(user, "user_id", out var actual) ||
                expectedUser != actual) return null;
            return new ExternalIdentityKey("vk", "oauth:" + clientId, actual);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException ||
            ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return null;
        }
    }

    private async Task<JsonDocument?> Post(string path, Dictionary<string, string> fields, CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(fields);
        using var response = await http.PostAsync(Origin + path, body, ct);
        if (!response.IsSuccessStatusCode) return null;
        return JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
    }

    private static bool Valid(JsonElement root) =>
        TelegramIdentityProof.UniqueObject(root) && !root.TryGetProperty("error", out _);
    private static bool Subject(JsonElement root, string name, out string id)
    {
        id = "";
        if (!root.TryGetProperty(name, out var value)) return false;
        id = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" :
            value.ValueKind == JsonValueKind.Number && value.TryGetUInt64(out var n) ? n.ToString(CultureInfo.InvariantCulture) : "";
        return TelegramIdentityProof.PositiveId(id);
    }
    private static bool Bounded(string? text, int max) =>
        !string.IsNullOrWhiteSpace(text) && text.Length <= max && !text.Any(char.IsControl);
    private static bool Pkce(string? text) => text is { Length: >= 43 and <= 128 } &&
        text.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '.' or '_' or '~');
    public void Dispose() => http.Dispose();
}
