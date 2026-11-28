using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Models.Autorization.Services;
using MysticEgo.Soul;

namespace MysticEgo.Api.Orchestra;

/// <summary>Internal reference bridge, not an HTTP action or a registered service.
/// A caller must authenticate the event using provider proof and claim its durable ingress receipt
/// before invoking this bridge. No platform credentials or account IDs enter the world envelope.
/// World/identity publication and revocation are not one transaction; do not enable live delivery.
/// </summary>
public sealed class MessengerWorldBridge(MessengerIdentityService identities, SpiritualCommunicationGateway world, TimeProvider clock)
{
    private const int MaxSelections = 256;
    private readonly object gate = new();
    private readonly Dictionary<(Guid Account,string Provider,string Bot,string Subject), Selection> selections = new();
    private sealed record Selection(Guid Revision,DateTimeOffset Expires,IReadOnlyDictionary<string,string> Targets);
    public sealed record Reply(string Text, Guid? QueuedMessageId = null);

    public async Task<Reply?> Handle(MessengerRealm realm, PrivateMessengerEvent message, CancellationToken ct = default)
    {
        if (!realm.Valid || !message.Sender.PrivateHumanChat || message.Sender.Provider != realm.Provider ||
            message.Sender.BotId != realm.BotId || message.Occurred < clock.GetUtcNow().AddMinutes(-10) ||
            message.Occurred > clock.GetUtcNow().AddSeconds(30)) return null;
        var text = message.Text;
        var nearby = text == "/nearby";
        var friends = text == "/friends";
        var talk = text.StartsWith("/talk ", StringComparison.Ordinal);
        if (!nearby && !friends && !talk) return null;
        // Fresh DB resolution per command; never cache identity authorization.
        var account = await identities.Resolve(realm,message.Sender,ct);
        if (account is null) return new("Подключите чат-бот в настройках безопасности Mystic Ego.");
        var key = (account.Value,realm.Provider,realm.BotId,message.Sender.UserId);
        if (nearby || friends)
        {
            var page = world.Discover(account.Value,nearby?SpiritualContactFilter.Nearby:SpiritualContactFilter.Friends);
            lock (gate)
            {
                var now = clock.GetUtcNow();
                foreach (var expired in selections.Where(x=>x.Value.Expires<=now).Select(x=>x.Key).ToArray())
                    selections.Remove(expired);
                selections.Remove(key);
                if (page.Contacts.Count == 0) return new("Доступных собеседников нет.");
                if (selections.Count >= MaxSelections) return new("Связь временно недоступна.");
                var targets = new Dictionary<string,string>(StringComparer.Ordinal);
                var lines = new List<string>{nearby?"Доступные собеседники в области:":"Доступные друзья:"};
                foreach (var contact in page.Contacts)
                {
                    string handle;
                    do { handle=Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(); }
                    while(!targets.TryAdd(handle,contact.EntityId));
                    lines.Add(handle+" — "+contact.Name+(contact.Kind==SpiritualParticipantKind.Npc?" (NPC)":" (игрок)"));
                }
                selections[key]=new(page.Revision,now.AddMinutes(5),targets);
                lines.Add("Отправьте /talk <код> <сообщение>. Код действует пять минут или до изменения состояния мира.");
                return new(string.Join("\n",lines));
            }
        }

        var separator=text.IndexOf(' ',6);
        if(separator<0||separator==text.Length-1)return new("Формат: /talk <код> <сообщение>. Сначала /nearby или /friends.");
        var handleText=text[6..separator];
        if(handleText.Length!=16||handleText.Any(c=>!(c is >= '0' and <= '9' or >= 'a' and <= 'f')))
            return new("Собеседник недоступен. Обновите список.");
        Selection? selection;
        string? target;
        lock(gate)
        {
            if(!selections.TryGetValue(key,out selection)||selection.Expires<=clock.GetUtcNow()||
                !selection.Targets.TryGetValue(handleText,out target))
                return new("Собеседник недоступен. Обновите список.");
        }
        // Scope stable event IDs to the verified provider/bot/private sender. Same event/content
        // retries cannot create a second message in the local journal. A trusted ingress receipt
        // is still required because account reassignment changes the journal's owner namespace.
        var source=JsonSerializer.Serialize(new[]{realm.Provider,realm.BotId,message.Sender.UserId,message.EventId});
        var digest=SHA256.HashData(Encoding.UTF8.GetBytes(source));
        var requestId=new Guid(digest.AsSpan(0,16));
        var result=world.SendAtRevision(account.Value,selection.Revision,new(requestId,target!,text[(separator+1)..]));
        return result.Status==SpiritualSendStatus.Accepted
            ?new("Сообщение принято в очередь духовной связи.",result.MessageId)
            :new("Собеседник недоступен. Обновите список.");
    }
}
