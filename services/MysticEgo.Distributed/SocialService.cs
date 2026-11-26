using System.Security.Claims;
using Models.Data;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

public interface ISocialAccessPolicy
{
    string? Actor(ClaimsPrincipal user);
    string Level(ClaimsPrincipal user);
    bool OrganizationAdmin(ClaimsPrincipal user);
    bool Developer(ClaimsPrincipal user);
}
public sealed class SocialAccessPolicy : ISocialAccessPolicy
{
    public string? Actor(ClaimsPrincipal user) => user.Identity?.IsAuthenticated == true && user.FindFirst("token_use")?.Value != "device"
        ? user.Identity.Name ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value : null;
    public string Level(ClaimsPrincipal user) => user.IsInRole("system.developer") || user.IsInRole("global.manager") ? "global"
        : user.IsInRole("local.admin") ? "local" : "agent";
    public bool OrganizationAdmin(ClaimsPrincipal user) => EcosystemHierarchy.IsDeveloper(user) || user.IsInRole("global.manager");
    public bool Developer(ClaimsPrincipal user) => EcosystemHierarchy.IsDeveloper(user);
}
public interface ISocialService
{
    Task<object> Directory(CancellationToken ct);
    Task<EmployeeAffiliation> SetAffiliation(EmployeeAffiliationEdit edit, string actor, CancellationToken ct);
    Task<RoleAppearance> SetAppearance(RoleAppearanceEdit edit, CancellationToken ct);
    Task<object> Profile(string user, string level, CancellationToken ct);
    Task<object> UpdateProfile(string user, string level, ProfileEdit edit, CancellationToken ct);
    Task<object> Notifications(string user, CancellationToken ct);
    Task<SocialNotification> ReadNotification(string user, Guid id, CancellationToken ct);
    Task<object> Friends(string user, CancellationToken ct);
    Task<FriendRequest> RequestFriend(string user, FriendEdit edit, CancellationToken ct);
    Task<FriendRequest> AcceptFriend(string user, Guid id, CancellationToken ct);
    Task<object> Messages(string user, CancellationToken ct);
    Task<SocialMessage> SendMessage(string user, MessageEdit edit, CancellationToken ct);
}
public sealed class SocialService(NodeState state) : ISocialService
{
    private static readonly SemaphoreSlim Writes = new(1, 1);
    private static bool UrlOk(string? text) => string.IsNullOrWhiteSpace(text) || (text.Length <= 500 && Uri.TryCreate(text,UriKind.Absolute,out var uri) && uri.Scheme == Uri.UriSchemeHttps);
    private static bool ColorOk(string? color) => string.IsNullOrEmpty(color) || System.Text.RegularExpressions.Regex.IsMatch(color,"^#[0-9a-fA-F]{6}$");
    private static int Completeness(SocialProfile profile) => new[] { profile.DisplayName, profile.Bio, profile.AvatarUrl, profile.CoverUrl, profile.BackgroundUrl }.Count(x=>!string.IsNullOrWhiteSpace(x)) * 20;

    public async Task<object> Directory(CancellationToken ct)
    {
        var profiles=await state.Data.ListAsync<SocialProfile>(2000,ct);
        var games=(await state.Data.ListAsync<EtherAccount>(2000,ct)).ToDictionary(x=>x.UserId,x=>x);
        var affiliations=(await state.Data.ListAsync<EmployeeAffiliation>(2000,ct)).ToDictionary(x=>x.UserId,x=>x);
        var styles=(await state.Data.ListAsync<RoleAppearance>(100,ct)).ToDictionary(x=>x.RoleCode,x=>x);
        return profiles.Where(x=>x.Discoverable).Select(x=>
        {
            var hasGame=games.TryGetValue(x.UserId,out var game);
            var staff=x.Level is "global" or "local";
            affiliations.TryGetValue(x.UserId,out var affiliation);
            styles.TryGetValue(x.Level,out var style);
            return new {
                nickname=x.DisplayName,userId=x.UserId,rank=hasGame?game!.Rank:"F",gameLevel=hasGame?game!.Level:1,
                accountScope=staff?"employee":"audit",avatarUrl=x.AvatarUrl,accent=style?.Color??x.Accent,
                role=x.Level,roleLabel=style?.Label??(staff?"Сотрудник":"Пользователь"),
                network=staff?affiliation?.Network:null,club=staff?affiliation?.Club:null,position=staff?affiliation?.Position:null
            };
        }).Take(100).ToArray();
    }
    public async Task<EmployeeAffiliation> SetAffiliation(EmployeeAffiliationEdit edit,string actor,CancellationToken ct)
    {
        if(edit.UserId.Length is <2 or >128 || edit.Network.Length>90 || edit.Club.Length>90 || edit.Position.Length>100)
            throw new NodeApiException(400,"Invalid affiliation");
        var existing=(await state.Data.ListAsync<EmployeeAffiliation>(2000,ct)).FirstOrDefault(x=>x.UserId==edit.UserId);
        var affiliation=existing??new EmployeeAffiliation {Id=Guid.NewGuid(),UserId=edit.UserId};
        affiliation.Network=edit.Network.Trim();affiliation.Club=edit.Club.Trim();affiliation.Position=edit.Position.Trim();affiliation.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(affiliation,ct);state.Log(actor,"social.affiliation",edit.UserId,true);
        return affiliation;
    }
    public async Task<RoleAppearance> SetAppearance(RoleAppearanceEdit edit,CancellationToken ct)
    {
        if(edit.RoleCode is not ("global" or "local" or "agent") || edit.Label.Length>50 || !ColorOk(edit.Color))
            throw new NodeApiException(400,"Invalid role appearance");
        var existing=(await state.Data.ListAsync<RoleAppearance>(100,ct)).FirstOrDefault(x=>x.RoleCode==edit.RoleCode);
        var style=existing??new RoleAppearance {Id=Guid.NewGuid(),RoleCode=edit.RoleCode};
        style.Color=edit.Color;style.Label=edit.Label;await state.Data.PutAsync(style,ct);
        return style;
    }
    public async Task<object> Profile(string user,string level,CancellationToken ct)
    {
        var profile=await FindProfile(user,ct);
        return new {profile,completion=profile is null?0:Completeness(profile),level};
    }
    public async Task<object> UpdateProfile(string user,string level,ProfileEdit edit,CancellationToken ct)
    {
        if((edit.StatusText?.Length ?? 0) > 100 || edit.StatusText?.Any(char.IsControl) == true ||
            edit.DisplayName.Length>60||edit.Bio.Length>600||!UrlOk(edit.AvatarUrl)||!UrlOk(edit.BackgroundUrl)||!UrlOk(edit.CoverUrl)||!ColorOk(edit.Accent))
            throw new NodeApiException(400,"Недопустимые поля профиля. Используйте HTTPS для изображений.");
        var profile=await FindProfile(user,ct)??new SocialProfile{Id=Guid.NewGuid(),UserId=user};
        profile.DisplayName=edit.DisplayName.Trim();profile.Bio=edit.Bio.Trim();profile.AvatarUrl=edit.AvatarUrl??"";
        profile.BackgroundUrl=edit.BackgroundUrl??"";profile.CoverUrl=edit.CoverUrl??"";profile.Accent=edit.Accent??"#a16bff";
        if (edit.StatusText is not null) profile.StatusText = edit.StatusText.Trim();
        profile.Level=level;profile.Discoverable=edit.Discoverable;profile.UpdatedAt=DateTimeOffset.UtcNow;
        await state.Data.PutAsync(profile,ct);state.Log(user,"profile.update",user,true);
        return new {profile,completion=Completeness(profile)};
    }
    public Task<SocialProfile?> FindProfile(string user, CancellationToken ct) =>
        Guid.TryParse(user, out var id) ? state.Data.GetAsync<SocialProfile>(id, ct) : FindLegacyProfile(user, ct);

    private async Task<SocialProfile?> FindLegacyProfile(string user, CancellationToken ct) =>
        (await state.Data.ListAsync<SocialProfile>(5000, ct)).FirstOrDefault(x => x.UserId == user);

    public async Task<SocialProfile> EnsureAccount(Guid id, string nickname, string login, string level, CancellationToken ct)
    {
        await Writes.WaitAsync(ct);
        try
        {
            var profile = await state.Data.GetAsync<SocialProfile>(id, ct);
            if (profile is not null && profile.GameNickname == nickname && profile.Login == login && profile.Level == level) return profile;
            profile ??= new SocialProfile
            {
                Id = id, AccountId = id, UserId = id.ToString(), DisplayName = nickname, Discoverable = true
            };
            profile.GameNickname = nickname;
            profile.Login = login;
            profile.Level = level;
            await state.Data.PutAsync(profile, ct);
            return profile;
        }
        finally { Writes.Release(); }
    }

    public async Task<SocialProfile[]> FindPeople(string query, Guid actor, CancellationToken ct)
    {
        if (Guid.TryParse(query, out var id))
        {
            var user = await state.Data.GetAsync<SocialProfile>(id, ct);
            return user is not null && id != actor ? [user] : [];
        }
        var profiles = await state.Data.ListAsync<SocialProfile>(5000, ct);
        return profiles.Where(x => x.AccountId is { } id && id != actor && x.Discoverable &&
            x.GameNickname.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.GameNickname).Take(40).ToArray();
    }

    public async Task<SocialMessage[]> Conversation(string user, string peer, DateTimeOffset? before, CancellationToken ct)
    {
        var friends = await state.Data.ListAsync<FriendRequest>(5000, ct);
        if (!friends.Any(f => f.Status == "accepted" && SamePair(f, user, peer)))
            throw new NodeApiException(403, "Чат открывается после подтверждения контакта.");
        return (await state.Data.ListAsync<SocialMessage>(5000, ct))
            .Where(x => ((x.Sender == user && x.Recipient == peer) || (x.Sender == peer && x.Recipient == user)) &&
                (before is null || x.At < before))
            .OrderByDescending(x => x.At).Take(60).Reverse().ToArray();
    }

    private static bool SamePair(FriendRequest request, string user, string peer) =>
        (request.Sender == user && request.Recipient == peer) || (request.Recipient == user && request.Sender == peer);

    public async Task<object> Notifications(string user,CancellationToken ct) =>
        (await state.Data.ListAsync<SocialNotification>(5000,ct)).Where(x=>x.Recipient==user).OrderByDescending(x=>x.At).Take(100).ToArray();
    public async Task<SocialNotification> ReadNotification(string user,Guid id,CancellationToken ct)
    {
        var item=await state.Data.GetAsync<SocialNotification>(id,ct);
        if(item is null||item.Recipient!=user)throw new NodeApiException(404,"Notification not found");
        item.Read=true;await state.Data.PutAsync(item,ct);return item;
    }
    public async Task<object> Friends(string user,CancellationToken ct) =>
        (await state.Data.ListAsync<FriendRequest>(5000,ct)).Where(x=>x.Sender==user||x.Recipient==user).OrderByDescending(x=>x.At).ToArray();
    public async Task<FriendRequest> RequestFriend(string user,FriendEdit edit,CancellationToken ct)
    {
        var recipient=edit.Recipient.Trim();
        if(recipient.Length is <2 or >128 || recipient==user)throw new NodeApiException(400,"Invalid friend request");
        await Writes.WaitAsync(ct);
        try
        {
            var friends = await state.Data.ListAsync<FriendRequest>(5000, ct);
            if (friends.Any(x => SamePair(x, user, recipient) && x.Status is "pending" or "accepted"))
                throw new NodeApiException(409, "Запрос уже отправлен или контакт добавлен.");
            var request = new FriendRequest { Id = Guid.NewGuid(), Sender = user, Recipient = recipient };
            await state.Data.PutAsync(request, ct);
            await Notify(recipient, user, "friend.request", "отправил запрос в контакты", ct);
            return request;
        }
        finally { Writes.Release(); }
    }
    public async Task<FriendRequest> AcceptFriend(string user,Guid id,CancellationToken ct)
    {
        await Writes.WaitAsync(ct);
        try
        {
            var request = await state.Data.GetAsync<FriendRequest>(id, ct);
            if (request is null || request.Recipient != user)
                throw new NodeApiException(404, "Входящий запрос не найден.");
            if (request.Status == "accepted") return request;
            request.Status = "accepted";
            await state.Data.PutAsync(request, ct);
            await Notify(request.Sender, user, "friend.accepted", "подтвердил запрос", ct);
            return request;
        }
        finally { Writes.Release(); }
    }
    public async Task<object> Messages(string user,CancellationToken ct) =>
        (await state.Data.ListAsync<SocialMessage>(5000,ct)).Where(x=>x.DeletedAt is null&&(x.Sender==user||x.Recipient==user)).OrderByDescending(x=>x.At).Take(100).ToArray();
    public Task<SocialMessage> SendMessage(string user, MessageEdit edit, CancellationToken ct) =>
        CreateMessage(user, edit, Guid.NewGuid(), null, null, false, ct);

    public Task<SocialMessage> SendVoice(string user, string peer, Guid id, string mime, Guid? replyTo, CancellationToken ct) =>
        CreateMessage(user, new MessageEdit(peer, "", ReplyToId: replyTo), id, mime, null, false, ct);

    public Task<SocialMessage> ForwardMessage(string user, string peer, Guid id, SocialMessage source, CancellationToken ct) =>
        CreateMessage(user, new MessageEdit(peer, source.Text, source.StickerId), id, source.VoiceMime, source.Transcript, true, ct);

    public async Task<SocialMessage> OpenMessage(string actor, Guid id, CancellationToken ct)
    {
        var message = await state.Data.GetAsync<SocialMessage>(id, ct);
        if (message is null || message.DeletedAt is not null || (message.Sender != actor && message.Recipient != actor))
            throw new NodeApiException(404, "Сообщение не найдено.");
        return message;
    }

    public async Task<SocialMessage> EditMessage(string actor, Guid id, string? value, CancellationToken ct)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 2000)
            throw new NodeApiException(400, "Введите от 1 до 2000 символов.");

        await Writes.WaitAsync(ct);
        try
        {
            var message = await OpenMessage(actor, id, ct);
            if (message.Sender != actor || message.Forwarded || message.VoiceMime is not null ||
                message.StickerId is not null || string.IsNullOrWhiteSpace(message.Text))
                throw new NodeApiException(403, "Можно редактировать только своё текстовое сообщение.");
            if (message.Text == text) return message;
            message.Text = text;
            message.EditedAt = DateTimeOffset.UtcNow;
            await state.Data.PutAsync(message, ct);
            return message;
        }
        finally { Writes.Release(); }
    }

    public async Task<SocialMessage> DeleteMessage(string actor, Guid id, CancellationToken ct)
    {
        await Writes.WaitAsync(ct);
        try
        {
            var message = await state.Data.GetAsync<SocialMessage>(id, ct);
            if (message is null || message.Sender != actor)
                throw new NodeApiException(404, "Сообщение не найдено.");
            if (message.DeletedAt is not null) return message;
            message.DeletedAt = DateTimeOffset.UtcNow;
            message.Text = "";
            message.StickerId = null;
            message.VoiceMime = null;
            message.Transcript = null;
            message.ReplyToId = null;
            await state.Data.PutAsync(message, ct);
            return message;
        }
        finally { Writes.Release(); }
    }

    public async Task<SocialMessage> SaveTranscript(string actor, Guid id, string transcript, CancellationToken ct)
    {
        await Writes.WaitAsync(ct);
        try
        {
            var message = await OpenMessage(actor, id, ct);
            if (message.VoiceMime is null) throw new NodeApiException(404, "Аудио не найдено.");
            if (message.Transcript is not null) return message;
            message.Transcript = transcript;
            await state.Data.PutAsync(message, ct);
            return message;
        }
        finally { Writes.Release(); }
    }

    private async Task<SocialMessage> CreateMessage(string user, MessageEdit edit, Guid id, string? voiceMime,
        string? transcript, bool forwarded, CancellationToken ct)
    {
        var recipient = edit.Recipient.Trim();
        var text = edit.Text?.Trim() ?? "";
        if (recipient.Length is < 2 or > 128 || recipient == user || text.Length > 2000 ||
            (text.Length == 0 && edit.StickerId is null && voiceMime is null))
            throw new NodeApiException(400, "Недопустимое сообщение.");

        await Writes.WaitAsync(ct);
        SocialMessage message;
        try
        {
            var friends = await state.Data.ListAsync<FriendRequest>(5000, ct);
            if (!friends.Any(friend => friend.Status == "accepted" && SamePair(friend, user, recipient)))
                throw new NodeApiException(403, "Чат открывается после подтверждения контакта.");
            if (edit.ReplyToId is { } replyId)
            {
                var reply = await OpenMessage(user, replyId, ct);
                if (reply.Sender != recipient && reply.Recipient != recipient)
                    throw new NodeApiException(404, "Сообщение для ответа не найдено в этом чате.");
            }
            message = new SocialMessage
            {
                Id = id, Sender = user, Recipient = recipient, Text = text, StickerId = edit.StickerId,
                ReplyToId = edit.ReplyToId, VoiceMime = voiceMime, Transcript = transcript, Forwarded = forwarded
            };
            await state.Data.PutAsync(message, ct);
        }
        finally { Writes.Release(); }

        // Notification failure must not turn a committed message into a retriable send.
        try { await Notify(recipient, user, "message", "отправил сообщение", ct); }
        catch (Exception error) when (error is not OutOfMemoryException)
        { state.Log(user, "message.notification", id.ToString(), false, error.GetType().Name); }
        return message;
    }
    private async Task Notify(string recipient, string sender, string kind, string action, CancellationToken ct)
    {
        var name = (await FindProfile(sender, ct))?.GameNickname;
        await state.Data.PutAsync(new SocialNotification
        {
            Id = Guid.NewGuid(), Recipient = recipient, Kind = kind, Text = $"{(string.IsNullOrEmpty(name) ? sender : name)} {action}"
        }, ct);
    }
}
public static class SocialDependencyInjection
{
    public static IServiceCollection AddSocial(this IServiceCollection services)
    {
        services.AddSingleton<ISocialAccessPolicy,SocialAccessPolicy>();
        services.AddScoped<SocialService>();
        services.AddScoped<ISocialService>(provider => provider.GetRequiredService<SocialService>());
        return services;
    }
}
