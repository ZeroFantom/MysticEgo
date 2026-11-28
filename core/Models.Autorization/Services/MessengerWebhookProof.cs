using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Models.Autorization.Services;

/// <summary>Server-side normalized event. Never serialize or log the text or link code.</summary>
public sealed class PrivateMessengerEvent
{
    internal PrivateMessengerEvent(VerifiedMessengerSender sender, string eventId, DateTimeOffset occurred, string text)
    { Sender = sender; EventId = eventId; Occurred = occurred; Text = text; }
    public VerifiedMessengerSender Sender { get; }
    public string EventId { get; }
    public DateTimeOffset Occurred { get; }
    public string Text { get; }
}

public static class MessengerWebhookProof
{
    public static bool ValidSecret(string? secret) => secret is { Length: >= 16 and <= 256 } &&
        secret.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-');
    public static bool SecretMatches(string? configured, string? supplied) =>
        ValidSecret(configured) && ValidSecret(supplied) &&
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.ASCII.GetBytes(configured!)),
            SHA256.HashData(Encoding.ASCII.GetBytes(supplied!)));

    private static bool Object(JsonElement parent, string name, out JsonElement item) =>
        parent.TryGetProperty(name, out item) && TelegramIdentityProof.UniqueObject(item);
    private static string? Id(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var item) && item.ValueKind == JsonValueKind.Number &&
        item.TryGetInt64(out var value) && value > 0 ? value.ToString(CultureInfo.InvariantCulture) : null;
    private static bool Number(JsonElement parent, string name, long expected) =>
        parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt64(out var actual) && actual == expected;
    private static string? Text(JsonElement node, string key) => TelegramIdentityProof.Text(node,key);
    internal static bool Fresh(DateTimeOffset occurred, DateTimeOffset now) =>
        occurred >= now.AddMinutes(-10) && occurred <= now.AddSeconds(30);
    private static PrivateMessengerEvent? Build(MessengerRealm realm, string? user, string? eventId,
        JsonElement message, DateTimeOffset now)
    {
        if (user is null || eventId is not { Length: > 0 and <= 128 } ||
            eventId.Any(c => !(c is >= '!' and <= '~')) ||
            !message.TryGetProperty("date", out var date) || date.ValueKind != JsonValueKind.Number ||
            !date.TryGetInt64(out var unix) || unix < 0 || unix > 253402300799 ||
            Text(message,"text") is not { Length: > 0 and <= 4000 } text) return null;
        var occurred = DateTimeOffset.FromUnixTimeSeconds(unix);
        if (!Fresh(occurred, now)) return null;
        return new(new(realm.Provider, realm.BotId, user, true), eventId, occurred, text);
    }

    public static PrivateMessengerEvent? Telegram(MessengerRealm realm, string configuredSecret, string? header,
        JsonElement root, DateTimeOffset now)
    {
        if (!realm.Valid || realm.Provider != "telegram" || !SecretMatches(configuredSecret,header) ||
            !TelegramIdentityProof.UniqueObject(root) || !Object(root,"message",out var message) ||
            !Object(message,"from",out var sender) || !Object(message,"chat",out var chat) ||
            !sender.TryGetProperty("is_bot",out var bot) || bot.ValueKind != JsonValueKind.False ||
            Text(chat,"type") != "private" || message.TryGetProperty("sender_chat",out _) ||
            message.TryGetProperty("via_bot",out _) || message.TryGetProperty("forward_origin",out _)) return null;
        var user=Id(sender,"id");
        if (user is null || user!=Id(chat,"id")) return null;
        return Build(realm,user,Id(root,"update_id"),message,now);
    }

    public static bool VkEnvelope(MessengerRealm realm, string configuredSecret, JsonElement root) =>
        realm.Valid && realm.Provider == "vk" && TelegramIdentityProof.UniqueObject(root) &&
        Id(root,"group_id")==realm.BotId && SecretMatches(configuredSecret,Text(root,"secret"));

    public static PrivateMessengerEvent? Vk(MessengerRealm realm,string configuredSecret,JsonElement root,DateTimeOffset now)
    {
        if(!VkEnvelope(realm,configuredSecret,root)||Text(root,"type")!="message_new"||
            !Object(root,"object",out var payload)||!Object(payload,"message",out var message)||!Number(message,"out",0))
            return null;
        var user=Id(message,"from_id");
        if(user is null||user!=Id(message,"peer_id"))return null;
        return Build(realm,user,Text(root,"event_id"),message,now);
    }
}
