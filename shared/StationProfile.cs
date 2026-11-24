namespace MysticEgo.Deployment;

public sealed record StationCredential(string Kind, Guid IdentityId, string Secret);
public sealed record StationProfile(string Name, StationCredential[] Credentials, string PublicKey, string AvatarKey, string? LocalSsh=null, string? GlobalSsh=null, StationEndpoints? Endpoints=null)
{
    public static string[] RolesFor(string preset) => preset switch
    {
        "single" => ["global", "audit", "local", "agent"],
        "site" => ["local", "agent"],
        "local" => ["local"],
        "agent" => ["agent"],
        "home" => ["audit"],
        "global" => ["global"],
        _ => throw new ArgumentException("Неизвестный набор ролей")
    };
}
public sealed record StationRequest(string Name, string Preset, string? LocalSsh=null, string? GlobalSsh=null);
public sealed record StationReport(string[] Healthy, string? Error, StationLink? Link = null, string? Machine = null);
public sealed record StationLink(double? LatencyMs, double? Mbps, DateTimeOffset MeasuredAt);
public sealed record StationDecision(bool Enabled, string? Reason = null);

public sealed record StationEndpoints(string AuthUrl, string OrchestraUrl, string? GlobalUrl, string? LocalUrl,
    string PublicUrl, string ParentId, string? OwnerLogin, string Mode, bool AllowLanHttp = false, string? GlobalId = null, string? LocalId = null,
    string? ReleasePublicKey = null, string? ReleasePrivateKey = null, string? GlobalReleaseKey = null, string? LocalReleaseKey = null,
    string? ClubAddress = null, string? ClubDescription = null);
