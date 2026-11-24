using System.Security.Cryptography;

// Генерация тестовой пары JWT RSA без внешних криптографических утилит.
// Используйте только для локального стенда разработки, не для прода.
if (args.Length != 2)
{
    Console.Error.WriteLine("Использование: MysticEgo.KeyGenerator <закрытый.pem> <открытый.pem>");
    return 2;
}

var privatePath = Path.GetFullPath(args[0]);
var publicPath = Path.GetFullPath(args[1]);
if (string.Equals(privatePath, publicPath, StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Файлы закрытого и открытого ключей должны различаться.");
    return 2;
}

try
{
    Directory.CreateDirectory(Path.GetDirectoryName(privatePath)!);
    Directory.CreateDirectory(Path.GetDirectoryName(publicPath)!);
    if (File.Exists(privatePath))
    {
        // Восстановление открытого ключа из существующего закрытого ключа,
        // не меняя идентичность центра авторизации.
        using var existing = RSA.Create();
        existing.ImportFromPem(File.ReadAllText(privatePath));
        var derivedPublic = existing.ExportSubjectPublicKeyInfoPem();
        if (File.Exists(publicPath))
        {
            using var check = RSA.Create();
            check.ImportFromPem(File.ReadAllText(publicPath));
            if (!existing.ExportParameters(false).Modulus!.AsSpan().SequenceEqual(check.ExportParameters(false).Modulus))
                throw new InvalidOperationException("Открытый и закрытый ключи JWT не совпадают. Исправьте ключи вручную.");
        }
        else
        {
            File.WriteAllText(publicPath, derivedPublic);
            Console.WriteLine("Восстановлен открытый RSA-ключ JWT из существующего закрытого ключа.");
        }
        return 0;
    }

    if (File.Exists(publicPath))
        throw new InvalidOperationException("Открытый ключ существует, но закрытый отсутствует. Не заменяем пару автоматически: проверьте каталог .testbed.");

    using var rsa = RSA.Create(3072);
    var privatePem = rsa.ExportPkcs8PrivateKeyPem();
    var publicPem = rsa.ExportSubjectPublicKeyInfoPem();
    try
    {
        // CreateNew не позволяет перезаписать секрет при гонке процессов.
        using (var stream = new FileStream(privatePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream)) writer.Write(privatePem);
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(privatePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.WriteAllText(publicPath, publicPem);
    }
    catch
    {
        // Не удаляем потенциально существующий закрытый ключ при конфликте.
        throw;
    }
    Console.WriteLine("Тестовая пара RSA-ключей JWT создана без OpenSSL.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Ошибка подготовки тестовых JWT-ключей: " + ex.Message);
    return 1;
}
