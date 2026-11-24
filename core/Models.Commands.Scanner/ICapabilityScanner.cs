using System.Reflection;
using Models.Commands.SignalR;

namespace Models.Commands.Scanner;

/// <summary>
/// Превращает сборки в набор CapabilityDto.
/// Никогда не бросает исключений при сканировании — сломанные типы
/// и сборки логируются и пропускаются.
/// </summary>
public interface ICapabilityScanner
{
    IReadOnlyList<CapabilityDto> Scan(Assembly assembly);
    IReadOnlyList<CapabilityDto> Scan(IEnumerable<Assembly> assemblies);
    IReadOnlyList<CapabilityDto> ScanCurrentAppDomain();
}