using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using MysticEgo.Deployment;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

public sealed record Station(Guid Id, string Name, string[] Roles, bool Enabled, string? Machine = null);
public sealed record StationObservation(DateTimeOffset Seen, string[] Healthy, string? Error, StationLink? Link = null);

public sealed class StationRegistry(IConfiguration config, NodeOptions node, IHttpClientFactory http)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, StationObservation> reports = new();
    private readonly SemaphoreSlim createLock = new(1);
    private readonly string path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(node.DataPath))!, "stations.json");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private List<Station> Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<List<Station>>(File.ReadAllText(path), Json)! : [];
    private void Save(List<Station> stations)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(stations, Json));
        File.Move(path + ".tmp", path, true);
    }
    public object List()
    {
        lock (gate) return Load().Select(s => new { s.Id, s.Name, s.Roles, s.Enabled,
            report = reports.GetValueOrDefault(s.Id) }).ToArray();
    }
    public async Task<StationProfile> Create(StationRequest request, string authorization, CancellationToken ct, bool requireUniqueName = true, string? machine = null)
    {
        if (!Regex.IsMatch(request.Name ?? "", @"^[\p{L}\p{N} _.-]{1,64}$") || string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Имя ПК должно содержать 1–64 символа");
        var roles = StationProfile.RolesFor(request.Preset);
        foreach(var host in new[]{request.LocalSsh,request.GlobalSsh}.Where(h=>!string.IsNullOrWhiteSpace(h)))
            if(!Regex.IsMatch(host!,@"^[a-zA-Z0-9_][a-zA-Z0-9._-]*@[a-zA-Z0-9][a-zA-Z0-9.-]*$"))
                throw new ArgumentException("Адрес родителя должен иметь вид user@host");
        var publicKey = await File.ReadAllTextAsync(config["Authorization:Jwt:PublicKeyPath"]!, ct);
        var syncKey = config["Avatar:SyncKey"] ?? "";
        await createLock.WaitAsync(ct);
        var credentials = new List<StationCredential>();
        using var client = http.CreateClient();
        client.BaseAddress = new Uri(node.AuthUrl);
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authorization);
        try
        {
            lock (gate)
                if (requireUniqueName && Load().Any(s => s.Name.Equals(request.Name, StringComparison.OrdinalIgnoreCase)))
                    throw new ArgumentException("Такое имя ПК уже зарегистрировано");
            foreach (var role in roles)
            {
                using var result = await client.PostAsJsonAsync("/api/identities/provision", new { name = request.Name + "/" + role, kind = role }, ct);
                result.EnsureSuccessStatusCode();
                credentials.Add((await result.Content.ReadFromJsonAsync<StationCredential>(cancellationToken:ct))!);
            }
            lock (gate)
            {
                var stations = Load();
                stations.Add(new Station(credentials[0].IdentityId, request.Name, roles, true, machine));
                Save(stations);
            }
            return new(request.Name, credentials.ToArray(), publicKey, syncKey, request.LocalSsh, request.GlobalSsh);
        }
        catch
        {
            foreach (var id in credentials)
            {
                try { using var result = await client.PostAsync($"/api/identities/{id.IdentityId}/revoke",null); }
                catch (HttpRequestException) { }
            }
            throw;
        }
        finally { createLock.Release(); }
    }
    public bool MachineInUse(string machine, params string[] kinds)
    {
        lock (gate) return Load().Any(x => x.Machine == machine && x.Roles.Intersect(kinds).Any());
    }

    public StationObservation? Observation(Guid id)
    {
        lock (gate)
        {
            var report = reports.GetValueOrDefault(id);
            return report is not null && DateTimeOffset.UtcNow - report.Seen < TimeSpan.FromSeconds(30) ? report : null;
        }
    }
    public bool SetEnabled(Guid id, bool enabled) => UpdateNode(id, null, enabled);

    public bool UpdateNode(Guid id, string? name, bool enabled)
    {
        lock (gate)
        {
            var rows = Load();
            var index = rows.FindIndex(s => s.Id == id);
            if (index < 0) return false;
            rows[index] = rows[index] with { Enabled = enabled, Name = name ?? rows[index].Name };
            Save(rows);
            return true;
        }
    }

    public async Task Remove(Guid id, string authorization)
    {
        lock (gate)
        {
            var rows = Load();
            rows.RemoveAll(x => x.Id == id);
            Save(rows);
        }
        try
        {
            using var client = http.CreateClient();
            client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse(authorization);
            using var result = await client.PostAsync(node.AuthUrl.TrimEnd('/') + $"/api/identities/{id}/revoke", null);
        }
        catch (HttpRequestException) { }
    }
    public StationDecision? CheckIn(Guid id, StationReport report)
    {
        lock (gate)
        {
            var rows = Load();
            var index = rows.FindIndex(s => s.Id == id);
            if (index < 0) return null;
            var row = rows[index];
            var healthy = row.Roles.Intersect(report.Healthy ?? []).ToArray();
            string? reason = null;
            if (row.Roles.Any(role => role is "global" or "local" or "agent"))
            {
                if (report.Machine is null || !Regex.IsMatch(report.Machine, "^[a-f0-9]{64}$"))
                    reason = "Запустите узел из актуального комплекта MysticEgo.";
                else if (report.Machine == node.MachineFingerprint ||
                    rows.Any(other => other.Id != id && other.Machine == report.Machine && other.Roles.Any(role => role is "global" or "local" or "agent")))
                    reason = "На этом компьютере уже закреплена другая роль MysticEgo.";
                else if (row.Machine is not null && row.Machine != report.Machine)
                    reason = "Конфигурация уже закреплена за другим компьютером.";
                else if (row.Machine is null && row.Enabled)
                {
                    row = row with { Machine = report.Machine };
                    rows[index] = row;
                    Save(rows);
                }
            }
            if (reason is not null)
            {
                // Keep the live report from the real station if someone reuses its credentials elsewhere.
                if (row.Machine is null || !reports.ContainsKey(id)) reports[id] = new(DateTimeOffset.UtcNow, [], reason);
                return new(false, reason);
            }
            reports[id] = new(DateTimeOffset.UtcNow, healthy, report.Error is { Length: > 300 } e ? e[..300] : report.Error, report.Link);
            return new(row.Enabled);
        }
    }
}
