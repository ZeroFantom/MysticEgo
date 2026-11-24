using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Hub;

public sealed class LoggingHubErrorHandler : IHubErrorHandler
{
    private readonly ILogger<LoggingHubErrorHandler> _log;

    public LoggingHubErrorHandler(ILogger<LoggingHubErrorHandler> log) => _log = log;

    public void Handle(HubErrorContext context)
    {
        try
        {
            _log.LogError(
                context.Exception,
                "Hub {Hub}.{Method} error (conn={ConnectionId}, client={ClientId})",
                context.HubName,
                context.Method,
                context.ConnectionId ?? "—",
                context.ClientId?.ToString() ?? "—");
        }
        catch
        {
            // Логирование не должно падать
        }
    }
}