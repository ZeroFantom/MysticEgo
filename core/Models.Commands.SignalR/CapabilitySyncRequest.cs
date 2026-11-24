namespace Models.Commands.SignalR;

/// <summary>
/// Клиент отправляет весь свой каталог capabilities.
/// Сервер атомарно заменяет старый каталог для этого клиента.
/// </summary>
public sealed record CapabilitySyncRequest
{
    /// <summary>Полный список capabilities.</summary>
    public required IReadOnlyList<CapabilityDto> Capabilities { get; init; }

    /// <summary>Версия клиента (из assembly).</summary>
    public string ClientVersion { get; init; } = "0.0.0";

    /// <summary>Машиночитаемый идентификатор клиента (не имя хоста).</summary>
    public string MachineGuid { get; init; } = "";

    /// <summary>Момент генерации (UTC).</summary>
    public DateTime ReportedAt { get; init; } = DateTime.UtcNow;
}