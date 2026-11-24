namespace Models.Commands.SignalR;

/// <summary>Один параметр команды.</summary>
public sealed record ParameterDto
{
    /// <summary>Имя. Из имени параметра метода.</summary>
    public required string Name { get; init; }

    /// <summary>Полное CLR-имя типа: "System.String", "System.Int32".</summary>
    public required string ClrType { get; init; }

    /// <summary>Описание для LLM. Опционально.</summary>
    public string Description { get; init; } = "";

    /// <summary>Обязательный или нет.</summary>
    public bool Required { get; init; }

    /// <summary>Значение по умолчанию (в виде строки). null — нет дефолта.</summary>
    public string? Default { get; init; }

    /// <summary>Допустимые значения для enum-типов. null — не enum.</summary>
    public IReadOnlyList<string>? Values { get; init; }

    /// <summary>Семантический формат: "color", "uri", "path", "email".</summary>
    public string? Format { get; init; }

    /// <summary>Пример значения.</summary>
    public string? Example { get; init; }
}