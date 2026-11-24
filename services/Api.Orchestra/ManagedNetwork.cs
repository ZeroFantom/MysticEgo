using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Headers;
using System.Text.Json;
using MysticEgo.Deployment;
using MysticEgo.Distributed;
using Models.Data;

namespace MysticEgo.Api.Orchestra;

[Entity("mysticego.orchestra.application", 2)]
public sealed class OrganizationApplication
{
    [EntityKey] public Guid Id { get; set; }
    public Guid ApplicantId { get; set; }
    public string Applicant { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Address { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Website { get; set; } = "";
    public string Status { get; set; } = "pending";
    public string? DecisionNote { get; set; }
    public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed record ManagedNode(Guid Id, string Kind, string Name, Guid ParentId, Guid? OwnerId, string? OwnerLogin,
    Guid? ApplicationId, string PublicUrl, string Mode, bool Enabled, Guid[]? Members = null, string? Address = null, string? Description = null, string? Machine = null);
public sealed record NewManagedNode(string Kind, string Name, Guid? ParentId, string PublicUrl, string Mode,
    Guid? ApplicationId, Guid? OwnerId = null, bool AllowLanHttp = false, string? Address = null, string? Description = null);
public sealed record ManagedNodeEdit(string Name, bool Enabled);
public sealed record ManagedNodeStatus(ManagedNode Node, StationObservation? Report, int People, int Sites);

public sealed class ManagedNetwork(NodeState state, IConfiguration config, IHttpClientFactory factory, StationRegistry stations)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim mutations = new(1, 1);
    private readonly string folder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(state.Options.DataPath))!, "managed");
    private string Index => Path.Combine(folder, "nodes.json");
    public Guid RootId => Guid.TryParse(state.Options.NodeId, out var id) ? id : Guid.Empty;
    public string AuthUrl => state.Options.BrowserAuthUrl ?? state.Options.AuthUrl;
    public string OrchestraUrl => state.Options.PublicUrl ?? "http://127.0.0.1:5500";
    public ManagedNode[] Nodes() => File.Exists(Index)
        ? JsonSerializer.Deserialize<ManagedNode[]>(File.ReadAllText(Index), Json) ?? [] : [];
    public ManagedNode? Find(Guid id) => Nodes().FirstOrDefault(x => x.Id == id);
    public ManagedNode? Parent(ManagedNode node) => Find(node.ParentId);
    public ManagedNode? Organization(ManagedNode node) => node.Kind switch
    {
        "global" => node,
        "local" => Parent(node),
        "agent" => Parent(node) is { } local ? Parent(local) : null,
        _ => null
    };
    public bool CanManage(ManagedNode node, Guid actor, bool developer) =>
        node.Kind == "audit" ? node.OwnerId == actor : developer || Organization(node)?.OwnerId == actor;
    public bool CanRead(ManagedNode node, Guid actor, bool developer) =>
        developer || CanManage(node, actor, false) || Organization(node)?.Members?.Contains(actor) == true;

    public async Task<OrganizationApplication[]> Applications(Guid actor, bool developer, CancellationToken ct) =>
        (await state.Data.ListAsync<OrganizationApplication>(5000, ct))
        .Where(x => developer || x.ApplicantId == actor).OrderByDescending(x => x.SubmittedAt).ToArray();

    public async Task<OrganizationApplication> Apply(Guid actor, string login, string name, string description, string? address, string? contact, string? website, CancellationToken ct)
    {
        ValidateName(name);
        if (address?.Length > 250 || contact?.Length > 150 || website?.Length > 240)
            throw new NodeApiException(400, "Сократите адрес, контакт или ссылку организации.");
        if (description.Length > 1500) throw new NodeApiException(400, "Описание должно содержать до 1500 символов.");
        await mutations.WaitAsync(ct);
        try
        {
            if ((await Applications(actor, false, ct)).Any(x => x.Status is "pending" or "approved" &&
                x.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw new NodeApiException(409, "Заявка для этой организации уже существует.");
            var item = new OrganizationApplication
            {
                Id = Guid.NewGuid(), ApplicantId = actor, Applicant = login, Name = name.Trim(), Description = description.Trim(), Address = address?.Trim() ?? "",
                Contact = contact?.Trim() ?? "", Website = website?.Trim() ?? ""
            };
            await state.Data.PutAsync(item, ct);
            state.Log(login, "organization.apply", item.Id.ToString(), true, item.Name);
            return item;
        }
        finally { mutations.Release(); }
    }

    public async Task<OrganizationApplication> Decide(Guid id, bool approved, string? note, string actor, CancellationToken ct)
    {
        if (note?.Length > 500) throw new NodeApiException(400, "Комментарий должен содержать до 500 символов.");
        await mutations.WaitAsync(ct);
        try
        {
            var item = await state.Data.GetAsync<OrganizationApplication>(id, ct) ??
                throw new NodeApiException(404, "Заявка не найдена.");
            if (item.Status != "pending") throw new NodeApiException(409, "По заявке уже принято решение.");
            item.Status = approved ? "approved" : "rejected";
            item.DecisionNote = note?.Trim();
            await state.Data.PutAsync(item, ct);
            await state.Data.PutAsync(new SocialNotification
            {
                Id = Guid.NewGuid(), Recipient = item.ApplicantId.ToString(), Kind = "organization.decision",
                Text = approved ? $"Заявка «{item.Name}» одобрена. Оркестр подготовит Global." : $"Заявка «{item.Name}» отклонена."
            }, ct);
            state.Log(actor, "organization." + item.Status, id.ToString(), true, item.Name);
            return item;
        }
        finally { mutations.Release(); }
    }

    public async Task<ManagedNode> Create(NewManagedNode input, Guid actor, string login, bool developer,
        Guid? parentClaim, string authorization, CancellationToken ct)
    {
        ValidateName(input.Name);
        if (input.Address?.Length > 250 || input.Description?.Length > 1500)
            throw new NodeApiException(400, "Сократите адрес или описание площадки.");
        var centralLan = config.GetValue("Managed:AllowLanHttp", false);
        var localHttp = input.Kind == "local" && Uri.TryCreate(input.PublicUrl, UriKind.Absolute, out var lanAddress) && IsLan(lanAddress);
        ValidateUrl(input.PublicUrl, input.AllowLanHttp && centralLan || localHttp);
        ValidateUrl(AuthUrl, centralLan);
        ValidateUrl(OrchestraUrl, centralLan);
        await mutations.WaitAsync(ct);
        try
        {
            var all = Nodes();
            if (input.Kind == "global")
            {
                if (!developer || parentClaim is not null) throw new NodeApiException(403, "Global развёртывает Оркестр.");
                if (input.Mode is not ("native" or "docker")) throw new NodeApiException(400, "Выберите среду запуска.");
                if (input.ParentId is { } id && id != RootId) throw new NodeApiException(400, "Родитель Global — Оркестр.");
                var host = new Uri(input.PublicUrl);
                if (!host.IsLoopback && (host.Host.Equals(new Uri(OrchestraUrl).Host, StringComparison.OrdinalIgnoreCase) ||
                    all.Any(row => row.Kind == "global" && new Uri(row.PublicUrl).Host.Equals(host.Host, StringComparison.OrdinalIgnoreCase))))
                    throw new NodeApiException(409, "Разместите Global на отдельном сервере.");
                OrganizationApplication? application = null;
                if (input.ApplicationId is { } applicationId)
                {
                    application = await state.Data.GetAsync<OrganizationApplication>(applicationId, ct);
                    if (application?.Status != "approved") throw new NodeApiException(409, "Сначала одобрите заявку.");
                    if (all.Any(x => x.ApplicationId == applicationId)) throw new NodeApiException(409, "Global по этой заявке уже подготовлен.");
                }
                var ownerId = application?.ApplicantId ?? input.OwnerId;
                string? ownerLogin = application?.Applicant;
                if (ownerLogin is null && ownerId.HasValue)
                    ownerLogin = (await state.Data.GetAsync<SocialProfile>(ownerId.Value, ct))?.Login ??
                        throw new NodeApiException(404, "Пользователь должен сначала войти в Audit.");
                var endpoints = SignedEndpoints(new StationEndpoints(AuthUrl, OrchestraUrl, null, null,
                    host.GetLeftPart(UriPartial.Authority), RootId.ToString(), ownerLogin, input.Mode, centralLan));
                return await Provision(input.Name, "global", RootId, ownerId, application?.Id, endpoints,
                    authorization, all, login, ct, application?.Address ?? input.Address, application?.Description ?? input.Description);
            }
            if (input.Kind != "local") throw new NodeApiException(403, "Audit подключается владельцем. Agent выпускает Local.");
            var global = all.FirstOrDefault(x => x.Id == input.ParentId && x.Kind == "global" && x.Enabled);
            if (global is null || global.Id != parentClaim || !CanManage(global, actor, developer))
                throw new NodeApiException(403, "Выпустите конфигурацию Local из панели своего Global.");
            if (Report(global.Id)?.Healthy.Contains("global") != true) throw new NodeApiException(409, "Дождитесь подключения Global.");
            if (all.Any(x => x.ParentId == global.Id && x.PublicUrl.Equals(input.PublicUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)))
                throw new NodeApiException(409, "В этом Global уже есть площадка по указанному адресу.");
            var address = new Uri(input.PublicUrl);
            if (!address.IsLoopback && global.PublicUrl is { } parent && new Uri(parent).Host.Equals(address.Host, StringComparison.OrdinalIgnoreCase))
                throw new NodeApiException(409, "Разместите Local на другом компьютере.");
            var station = SignedEndpoints(new StationEndpoints(AuthUrl, OrchestraUrl, global.PublicUrl, null,
                address.GetLeftPart(UriPartial.Authority), global.Id.ToString(), global.OwnerLogin, "native", centralLan || localHttp,
                GlobalId: global.Id.ToString(), ClubAddress: input.Address?.Trim(), ClubDescription: input.Description?.Trim()));
            return await Provision(input.Name, "local", global.Id, global.OwnerId, null, station, await ServiceAuthorization(ct),
                all, login, ct, input.Address, input.Description);
        }
        finally { mutations.Release(); }
    }

    public async Task<StationProfile> AttachHome(Guid actor, string login, string machine, CancellationToken ct)
    {
        ValidateMachine(machine);
        await mutations.WaitAsync(ct);
        try
        {
            var all = Nodes();
            var current = all.FirstOrDefault(x => x.Kind == "audit" && x.OwnerId == actor && x.Machine == machine);
            if (current is not null) return await ReadProfile(current.Id, ct);
            var lan = config.GetValue("Managed:AllowLanHttp", false);
            var endpoints = new StationEndpoints(AuthUrl, OrchestraUrl, null, null, "http://127.0.0.1:5350",
                RootId.ToString(), login, "native", lan);
            var registered = await Provision("Audit " + machine[..8], "audit", RootId, actor, null,
                endpoints, await ServiceAuthorization(ct), all, login, ct, machine: machine);
            return await ReadProfile(registered.Id, ct);
        }
        finally { mutations.Release(); }
    }

    public async Task<ClubAssignment> IssueClub(Guid localId, Guid actor, string login, bool developer, CancellationToken ct)
    {
        var local = Find(localId);
        var global = local is null ? null : Organization(local);
        if (local is not { Kind: "local", Enabled: true } || global is not { Enabled: true } || !CanManage(local, actor, developer))
            throw new NodeApiException(403, "Конфигурацию Agent выпускает администратор Local.");
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var release = new ClubInvitation
        {
            Id = Guid.NewGuid(), Local = local.Id, Hash = Hash(code), ValidUntil = DateTimeOffset.UtcNow.AddDays(30)
        };
        await state.Data.PutAsync(release, ct);
        state.Log(login, "club.agent.distribution", local.Id.ToString(), true, local.Name);
        var profile = await ReadProfile(local.Id, ct);
        return new(local.Id, local.PublicUrl, global.Id, global.PublicUrl, OrchestraUrl, local.Name,
            release.Id, code, profile.Endpoints?.AllowLanHttp == true, release.ValidUntil);
    }

    public async Task<StationProfile> EnrollAgent(Guid localId, ClubJoin claim, CancellationToken ct)
    {
        ValidateMachine(claim.Machine);
        var invitation = await state.Data.GetAsync<ClubInvitation>(claim.InvitationId, ct);
        if (invitation?.Local != localId || invitation.ValidUntil < DateTimeOffset.UtcNow ||
            claim.Code.Length != 64 || !CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(invitation.Hash), Encoding.ASCII.GetBytes(Hash(claim.Code))))
            throw new NodeApiException(403, "Код комплекта Local недействителен или истёк.");
        await mutations.WaitAsync(ct);
        try
        {
            var all = Nodes();
            var local = all.FirstOrDefault(x => x.Id == localId && x.Kind == "local" && x.Enabled)
                ?? throw new NodeApiException(403, "Local отключён.");
            var global = Organization(local) ?? throw new NodeApiException(409, "Нет Global площадки.");
            if (!global.Enabled || Report(localId) is null) throw new NodeApiException(409, "Local пока не зарегистрирован в Оркестре.");
            if (claim.Machine == state.Options.MachineFingerprint || stations.MachineInUse(claim.Machine, "global", "local") ||
                all.Any(x => x.Kind == "agent" && x.Machine == claim.Machine && x.ParentId != localId))
                throw new NodeApiException(409, "Роль Agent запрещена на компьютере сервера или другой площадки.");
            var current = all.FirstOrDefault(x => x.Kind == "agent" && x.ParentId == localId && x.Machine == claim.Machine);
            if (current is not null) return await ReadProfile(current.Id, ct);
            if (all.Count(x => x.ParentId == localId && x.Kind == "agent") >= 400)
                throw new NodeApiException(429, "Достигнут предел рабочих станций этой площадки.");
            var localProfile = await ReadProfile(local.Id, ct);
            var globalProfile = await ReadProfile(global.Id, ct);
            var endpoints = new StationEndpoints(AuthUrl, OrchestraUrl, global.PublicUrl, local.PublicUrl,
                "http://127.0.0.1:5300", local.Id.ToString(), global.OwnerLogin, "native",
                localProfile.Endpoints?.AllowLanHttp == true, global.Id.ToString(), local.Id.ToString(),
                GlobalReleaseKey: globalProfile.Endpoints?.ReleasePublicKey, LocalReleaseKey: localProfile.Endpoints?.ReleasePublicKey,
                ClubAddress: local.Address, ClubDescription: local.Description);
            var name = System.Text.RegularExpressions.Regex.Replace(claim.Name?.Trim() ?? "", @"[^\p{L}\p{N} _.-]", "");
            if (name.Length == 0) name = "Agent " + claim.Machine[..8];
            if (name.Length > 64) name = name[..64];
            var registered = await Provision(name, "agent", localId, global.OwnerId, null, endpoints,
                await ServiceAuthorization(ct), all, "local:" + localId, ct, machine: claim.Machine);
            return await ReadProfile(registered.Id, ct);
        }
        finally { mutations.Release(); }
    }

    private async Task<ManagedNode> Provision(string name, string kind, Guid parentId, Guid? owner, Guid? applicationId,
        StationEndpoints endpoints, string bearer, ManagedNode[] current, string actor, CancellationToken ct,
        string? address = null, string? description = null, string? machine = null)
    {
        var profile = await stations.Create(new StationRequest(name.Trim(), kind == "audit" ? "home" : kind), bearer, ct, requireUniqueName: false, machine: machine);
        var id = profile.Credentials.Single().IdentityId;
        try
        {
            var node = new ManagedNode(id, kind, name.Trim(), parentId, owner, endpoints.OwnerLogin, applicationId,
                endpoints.PublicUrl, endpoints.Mode, true, Address: address?.Trim(), Description: description?.Trim(), Machine: machine);
            SavePrivate(ProfilePath(id), JsonSerializer.Serialize(profile with { Endpoints = endpoints }, Json));
            SavePrivate(Index, JsonSerializer.Serialize(current.Append(node), Json));
            state.Log(actor, "deployment.configuration", id.ToString(), true, kind + " · " + node.Name);
            return node;
        }
        catch
        {
            if (File.Exists(ProfilePath(id))) File.Delete(ProfilePath(id));
            await stations.Remove(id, bearer);
            throw;
        }
    }

    public async Task<StationProfile> RefreshDevice(Guid id, CancellationToken ct)
    {
        await mutations.WaitAsync(ct);
        try
        {
            var node = Find(id);
            if (node is not { Enabled: true }) throw new NodeApiException(404, "Конфигурация не найдена.");
            return await ReadProfile(node.Id, ct);
        }
        finally { mutations.Release(); }
    }

    private async Task<StationProfile> ReadProfile(Guid id, CancellationToken ct)
    {
        var path = ProfilePath(id);
        if (!File.Exists(path)) throw new NodeApiException(404, "Конфигурация не найдена.");
        var profile = JsonSerializer.Deserialize<StationProfile>(await File.ReadAllTextAsync(path, ct), Json)!;
        var node = Find(id);
        if (profile.Endpoints is not { } source || node is null) return profile;
        var endpoints = source;
        if (node.Kind is "local" or "global" && string.IsNullOrWhiteSpace(source.ReleasePrivateKey))
            endpoints = SignedEndpoints(source);
        if (node.Kind == "local" && string.IsNullOrWhiteSpace(endpoints.GlobalId))
            endpoints = endpoints with { GlobalId = node.ParentId.ToString(), ClubAddress = node.Address, ClubDescription = node.Description };
        if (node.Kind == "agent" && Parent(node) is { } local && Organization(node) is { } global)
        {
            var localKey = (await ReadProfile(local.Id, ct)).Endpoints?.ReleasePublicKey;
            var globalKey = (await ReadProfile(global.Id, ct)).Endpoints?.ReleasePublicKey;
            endpoints = endpoints with { GlobalId = global.Id.ToString(), LocalId = local.Id.ToString(),
                GlobalReleaseKey = globalKey, LocalReleaseKey = localKey, ClubAddress = local.Address, ClubDescription = local.Description };
        }
        if (source == endpoints) return profile;
        profile = profile with { Endpoints = endpoints };
        SavePrivate(path, JsonSerializer.Serialize(profile, Json));
        return profile;
    }

    private static StationEndpoints SignedEndpoints(StationEndpoints source)
    {
        using var key = RSA.Create(3072);
        return source with { ReleasePublicKey = key.ExportSubjectPublicKeyInfoPem(), ReleasePrivateKey = key.ExportPkcs8PrivateKeyPem() };
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(value)));
    private static void ValidateMachine(string machine)
    {
        if (machine is null || !System.Text.RegularExpressions.Regex.IsMatch(machine, "^[a-f0-9]{64}$"))
            throw new NodeApiException(400, "AuditDesktop не передал идентификатор компьютера.");
    }
    private static bool IsLan(Uri uri)
    {
        if (!System.Net.IPAddress.TryParse(uri.Host, out var ip)) return uri.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var parts = ip.GetAddressBytes();
        return uri.IsLoopback || parts.Length == 4 && (parts[0] == 10 || parts[0] == 192 && parts[1] == 168 ||
            parts[0] == 172 && parts[1] is >= 16 and <= 31);
    }

    public async Task<ManagedNode> Edit(Guid id, ManagedNodeEdit input, Guid actor, string login, bool developer, CancellationToken ct)
    {
        ValidateName(input.Name);
        await mutations.WaitAsync(ct);
        try
        {
            var list = Nodes();
            var node = list.FirstOrDefault(x => x.Id == id) ?? throw new NodeApiException(404, "Узел не найден.");
            if (!CanManage(node, actor, developer)) throw new NodeApiException(403, "Недостаточно прав для изменения узла.");
            if (!input.Enabled && list.Any(x => x.ParentId == id && x.Enabled))
                throw new NodeApiException(409, "Сначала отключите дочерние узлы.");
            if (input.Enabled && node.ParentId != RootId && list.FirstOrDefault(x => x.Id == node.ParentId)?.Enabled != true)
                throw new NodeApiException(409, "Сначала включите родительский узел.");
            var updated = node with { Name = input.Name.Trim(), Enabled = input.Enabled };
            if (!stations.UpdateNode(id, updated.Name, updated.Enabled)) throw new NodeApiException(404, "Профиль узла не найден.");
            SavePrivate(Index, JsonSerializer.Serialize(list.Select(x => x.Id == id ? updated : x), Json));
            state.Log(login, "node.edit", id.ToString(), true, updated.Enabled ? updated.Name + " · включён" : updated.Name + " · отключён");
            return updated;
        }
        finally { mutations.Release(); }
    }

    public async Task<ManagedNode> SetMember(Guid id, Guid member, bool allowed, Guid actor, string login, bool developer, CancellationToken ct)
    {
        await mutations.WaitAsync(ct);
        try
        {
            var list = Nodes();
            var node = list.FirstOrDefault(x => x.Id == id && x.Kind == "global") ??
                throw new NodeApiException(404, "Global не найден.");
            if (!CanManage(node, actor, developer)) throw new NodeApiException(403, "Нужны полномочия владельца.");
            if (member == node.OwnerId) throw new NodeApiException(400, "Доступ владельца определяется заявкой.");
            if (allowed && await state.Data.GetAsync<SocialProfile>(member, ct) is null)
                throw new NodeApiException(404, "Публичный ID пользователя не найден.");
            var members = (node.Members ?? []).ToHashSet();
            if (allowed) members.Add(member); else members.Remove(member);
            var updated = node with { Members = members.ToArray() };
            SavePrivate(Index, JsonSerializer.Serialize(list.Select(x => x.Id == id ? updated : x), Json));
            state.Log(login, allowed ? "global.member.add" : "global.member.remove", id.ToString(), true, member.ToString());
            return updated;
        }
        finally { mutations.Release(); }
    }

    public ManagedNodeStatus[] Topology(Guid actor, bool developer)
    {
        var nodes = Nodes();
        return nodes.Where(x => CanRead(x, actor, developer)).Select(x => new ManagedNodeStatus(x with { Machine = null, OwnerLogin = x.Kind == "audit" && x.OwnerId != actor ? null : x.OwnerLogin }, Report(x.Id),
            x.Kind == "global" ? (x.Members?.Length ?? 0) + (x.OwnerId.HasValue ? 1 : 0) : 0,
            x.Kind == "global" ? nodes.Count(local => local.Kind == "local" && local.ParentId == x.Id) : 0))
            .ToArray();
    }

    public StationObservation? Report(Guid id) => stations.Observation(id);
    public string ProfilePath(Guid id) => Path.Combine(folder, id.ToString("N") + ".station.json");

    private async Task<string> ServiceAuthorization(CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(state.Options.IdentityId) || string.IsNullOrWhiteSpace(state.Options.IdentitySecret))
            throw new NodeApiException(503, "Для выпуска дочерних узлов запустите Auth и Оркестр через центральное приложение.");
        using var client = factory.CreateClient("mysticego-logged-api");
        using var response = await client.PostAsJsonAsync(state.Options.AuthUrl.TrimEnd('/') + "/api/identities/token",
            new { identityId = state.Options.IdentityId, secret = state.Options.IdentitySecret }, ct);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        return new AuthenticationHeaderValue("Bearer", payload.GetProperty("accessToken").GetString()).ToString();
    }

    private static void ValidateName(string? name)
    {
        if (name is null || !System.Text.RegularExpressions.Regex.IsMatch(name.Trim(), @"^[\p{L}\p{N} _.-]{1,64}$"))
            throw new NodeApiException(400, "В имени допустимы буквы, цифры, пробелы, точки, дефисы и подчёркивания.");
    }
    internal static void ValidateUrl(string? url, bool allowLanHttp)
    {
        if (url is null || url.Length > 300 || !Uri.TryCreate(url, UriKind.Absolute, out var value) ||
            !string.IsNullOrEmpty(value.UserInfo) || value.AbsolutePath != "/" || value.Fragment.Length != 0 || value.Query.Length != 0 ||
            (value.Scheme != "https" && !(value.Scheme == "http" && (value.IsLoopback || allowLanHttp))))
            throw new NodeApiException(400, "Укажите базовый HTTPS-адрес. HTTP в локальной сети требует включения тестового режима центра.");
    }
    private void SavePrivate(string path, string content)
    {
        Directory.CreateDirectory(folder);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(tmp, options))
        using (var writer = new StreamWriter(file)) writer.Write(content);
        File.Move(tmp, path, true);
    }
}

[Entity("mysticego.club.enrollment", 1)]
public sealed class ClubInvitation
{
    [EntityKey] public Guid Id { get; set; }
    public Guid Local { get; set; }
    public string Hash { get; set; } = "";
    public DateTimeOffset ValidUntil { get; set; }
}
