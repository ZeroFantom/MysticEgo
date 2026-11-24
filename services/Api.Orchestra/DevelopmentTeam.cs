using System.Security.Claims;
using Models.Data;
using MysticEgo.Distributed;

namespace MysticEgo.Api.Orchestra;

[Entity("mysticego.orchestra.development.membership", 1)]
public sealed class DevelopmentMembership
{
    [EntityKey] public Guid Id { get; set; }
    public string Login { get; set; } = "";
    public string Status { get; set; } = "pending";
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class DevelopmentTeam(NodeState state, IConfiguration configuration)
{
    private readonly SemaphoreSlim gate = new(1, 1);

    public bool IsOwner(ClaimsPrincipal user)
    {
        if (!EcosystemHierarchy.IsDeveloper(user)) return false;
        var configured = configuration["DevelopmentTeam:OwnerId"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Guid.TryParse(configured, out var expected) && expected != Guid.Empty &&
                Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var actual) && actual == expected;
        return string.Equals(user.Identity?.Name, configuration["Authorization:Bootstrap:Login"] ?? "developer",
            StringComparison.OrdinalIgnoreCase);
    }

    public Task<DevelopmentMembership?> Find(Guid id, CancellationToken ct) =>
        state.Data.GetAsync<DevelopmentMembership>(id, ct);

    public Task<bool> IsMember(ClaimsPrincipal user, CancellationToken ct) =>
        user.Identity?.IsAuthenticated == true && user.FindFirstValue("token_use") != "device" &&
            Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? Check(id, ct) : Task.FromResult(false);

    private async Task<bool> Check(Guid id, CancellationToken ct) =>
        (await Find(id, ct))?.Status == "approved";

    public async Task<DevelopmentMembership[]> All(CancellationToken ct) =>
        (await state.Data.ListAsync<DevelopmentMembership>(5000, ct)).OrderByDescending(item => item.UpdatedAt).ToArray();

    public async Task<DevelopmentMembership?> Change(Guid id, string login, string next, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var item = await Find(id, ct);
            var allowed = (item?.Status, next) switch
            {
                (null or "rejected" or "revoked", "pending") => true,
                ("pending", "approved" or "rejected") => true,
                ("approved", "revoked") => true,
                _ => false
            };
            if (!allowed) return null;
            item ??= new DevelopmentMembership { Id = id, Login = login };
            item.Status = next;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            await state.Data.PutAsync(item, ct);
            state.Log(login, "development_team." + next, id.ToString(), true);
            return item;
        }
        finally { gate.Release(); }
    }
}
