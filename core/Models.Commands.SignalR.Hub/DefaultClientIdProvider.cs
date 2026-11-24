using Microsoft.AspNetCore.SignalR;

namespace Models.Commands.SignalR.Hub;

public sealed class DefaultClientIdProvider : IClientIdProvider
{
    public Guid? GetClientId(HubCallerContext context)
    {
        if (context is null) return null;

        var raw = context.UserIdentifier;
        return Guid.TryParse(raw, out var id) ? id : null;
    }
}