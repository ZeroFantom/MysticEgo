// ════════════════════════════════════════════════════════════════════
//  Models.Identifiers — единый файл
//  Фундамент системы: все ID выводятся из имён, а не из атрибутов.
// ════════════════════════════════════════════════════════════════════
//
//  csproj:
//  <Project Sdk="Microsoft.NET.Sdk">
//    <PropertyGroup>
//      <TargetFramework>net10.0</TargetFramework>
//      <Nullable>enable</Nullable>
//      <ImplicitUsings>enable</ImplicitUsings>
//      <RootNamespace>Models.Identifiers</RootNamespace>
//      <LangVersion>latest</LangVersion>
//    </PropertyGroup>
//
//    <ItemGroup>
//      <ProjectReference Include="..\Models.Commands\Models.Commands.csproj" />
//    </ItemGroup>
//  </Project>
//
//  Зависимость Models.Commands нужна только ради ICommand.
//  Если убрать From<T>() — можно жить вообще без зависимостей.
//
//  ПРАВИЛО ИМЕНОВАНИЯ
//  ────────────────────────────────────────────────────────────────
//  PascalCase → lower.dot.case
//
//    AudioListCommand          → audio.list
//    RgbMouseCommand           → rgb.mouse
//    RGBCommand                → rgb
//    AudioRGBCommand           → audio.rgb
//    AudioRGBListCommand       → audio.rgb.list
//    Http2ServerCommand        → http2.server
//    XMLHttpRequestCommand     → xml.http.request
//
//  РОЛИ ID
//  ────────────────────────────────────────────────────────────────
//  CommandId    — команда клиента.   "audio.list"
//  PermissionId — право (контроллер). "audio"
//  ToolId       — инструмент (метод). "audio.list"
//
//  КОНТРАКТ: ToolId.Value == CommandId.Value
//  Один и тот же ID — и на сервере (как tool), и на клиенте (как command).
// ════════════════════════════════════════════════════════════════════



// ════════════════════════════════════════════════════════════════════
//  IdentifierException
// ════════════════════════════════════════════════════════════════════

namespace Models.Commands;

/// <summary>
/// Нарушение контракта идентификатора: пустое значение, неверный формат,
/// несовпадение с конвенцией именования.
/// </summary>
public sealed class IdentifierException : Exception
{
    public IdentifierException(string message) : base(message) { }

    public IdentifierException(string message, Exception inner) : base(message, inner) { }
}

// ════════════════════════════════════════════════════════════════════
//  IdentifierConvention
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  CommandId
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  PermissionId
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  ToolId
// ════════════════════════════════════════════════════════════════════

// ════════════════════════════════════════════════════════════════════
//  JSON конвертеры
// ════════════════════════════════════════════════════════════════════