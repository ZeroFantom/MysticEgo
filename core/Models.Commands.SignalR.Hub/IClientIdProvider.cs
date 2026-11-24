using Microsoft.AspNetCore.SignalR;

namespace Models.Commands.SignalR.Hub;

/// <summary>
/// Провайдер текущего ClientId из HubCallerContext.
/// По умолчанию — UserIdentifier, если это валидный guid.
/// Можно переопределить, если у тебя другой способ.
/// </summary>
public interface IClientIdProvider
{
    Guid? GetClientId(HubCallerContext context);
}