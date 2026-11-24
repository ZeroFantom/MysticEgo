namespace MysticEgo.Distributed;

public sealed record ClubAssignment(Guid LocalId, string LocalUrl, Guid GlobalId, string GlobalUrl, string OrchestraUrl,
    string Name, Guid InvitationId, string Code, bool AllowLanHttp, DateTimeOffset ValidUntil);
public sealed record ClubJoin(Guid InvitationId, string Code, string Machine, string Name);
