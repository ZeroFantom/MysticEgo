namespace Models.Commands.SignalR.Hub;

/// <summary>
/// Стратегия обработки ошибок хаба. По умолчанию — логирование.
/// Можешь подменить реализацию, чтобы слать алерты, метрики, писать в Sentry.
/// </summary>
public interface IHubErrorHandler
{
    /// <summary>
    /// Вызывается при ошибке в hub-методе. Не должен бросать.
    /// </summary>
    void Handle(HubErrorContext context);
}