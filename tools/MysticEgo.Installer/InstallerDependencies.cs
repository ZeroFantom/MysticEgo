using System.Formats.Tar;
using System.IO.Compression;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

/// <summary>
/// Per-machine, Docker-free toolchain bootstrap. Official release metadata and checksums are
/// verified before extracting SDK/Node into a private, user-scoped cache. No shell install scripts.
/// This is a build prerequisite installer, not an installer for OS libraries or drivers.
/// </summary>
internal static class InstallerDependencies
{
    private const long MaxArchiveBytes = 1_200_000_000;
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(20);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task<bool> EnsureAsync(bool sdk, bool node, bool installMissing, CancellationToken ct = default)
    {
        if (!sdk && !node) return true;
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsLinux())
            return Fail("Автоматическая установка зависимостей доступна для Windows и Linux.");

        var rid=GetRid();
        var cachedSdk=Path.Combine(CacheRoot,"dotnet-10",rid);
        var cachedNode=Path.Combine(CacheRoot,"node-lts",rid);
        if(!OperatingSystem.IsWindows()) cachedNode=Path.Combine(cachedNode,"bin");
        if(sdk && Directory.Exists(cachedSdk)) AddToPath(cachedSdk,true);
        if(node && Directory.Exists(cachedNode)) AddToPath(cachedNode);
        if (sdk && !await HasSdk10Async(ct))
        {
            if (!installMissing) return Fail("Не найден .NET SDK 10. При обычной установке он будет установлен автоматически (не используйте --preflight-only).");
            if (!await InstallSdkAsync(ct)) return false;
            if (!await HasSdk10Async(ct)) return Fail("После установки .NET SDK 10 не обнаружен.");
        }
        if (node && !await HasNodeAndNpmAsync(ct))
        {
            if (!installMissing) return Fail("Не найдены Node.js LTS/npm. При обычной установке они будут установлены автоматически (не используйте --preflight-only).");
            if (!await InstallNodeAsync(ct)) return false;
            if (!await HasNodeAndNpmAsync(ct)) return Fail("После установки Node.js/npm не обнаружены.");
        }
        return true;
    }

    private static string CacheRoot
    {
        get
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(local)) local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(local, "MysticEgo", "toolchains");
        }
    }

    private static string GetRid()
    {
        var platform = OperatingSystem.IsWindows() ? "win" : "linux";
        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("Only x64 and arm64 are supported for automatic SDK/Node installation.")
        };
        return $"{platform}-{arch}";
    }

    private static void AddToPath(string directory, bool dotnet = false)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!path.Split(Path.PathSeparator).Contains(directory, StringComparer.OrdinalIgnoreCase))
            Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + path);
        if (dotnet)
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", directory);
            Environment.SetEnvironmentVariable("DOTNET_MULTILEVEL_LOOKUP", "0");
        }
    }

    private static async Task<bool> HasSdk10Async(CancellationToken ct)
    {
        var result = await CaptureAsync("dotnet", ["--list-sdks"], ct);
        if (result.ExitCode == 0 && result.Stdout.Split('\n').Any(x => x.TrimStart().StartsWith("10.", StringComparison.Ordinal))) return true;
        var cache = Path.Combine(CacheRoot, "dotnet-10", GetRid());
        if (!File.Exists(Path.Combine(cache, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"))) return false;
        AddToPath(cache, true);
        result = await CaptureAsync("dotnet", ["--list-sdks"], ct);
        return result.ExitCode == 0 && result.Stdout.Split('\n').Any(x => x.TrimStart().StartsWith("10.", StringComparison.Ordinal));
    }

    private static async Task<bool> HasNodeAndNpmAsync(CancellationToken ct)
    {
        if (await ValidNodeAndNpmAsync(ct)) return true;
        var cache = Path.Combine(CacheRoot, "node-lts", GetRid());
        var bin = OperatingSystem.IsWindows() ? cache : Path.Combine(cache, "bin");
        if (!File.Exists(Path.Combine(bin, OperatingSystem.IsWindows() ? "node.exe" : "node"))) return false;
        AddToPath(bin);
        return await ValidNodeAndNpmAsync(ct);
    }

    private static async Task<bool> ValidNodeAndNpmAsync(CancellationToken ct)
    {
        var nodeResult = await CaptureAsync("node", ["--version"], ct);
        if (nodeResult.ExitCode != 0 || !Version.TryParse(nodeResult.Stdout.Trim().TrimStart('v', 'V').Split('-')[0], out var version) || version.Major < 22)
            return false;
        var npmResult = await CaptureAsync("npm", ["--version"], ct);
        return npmResult.ExitCode == 0;
    }

    private static async Task<bool> InstallSdkAsync(CancellationToken ct)
    {
        try
        {
            Console.WriteLine("[Prerequisites] Загрузка официального .NET 10 SDK...");
            using var http = CreateHttp();
            const string metadata = "https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(metadata, ct));
            var rid = GetRid();
            var extension = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
            var sdkFiles = doc.RootElement.GetProperty("releases").EnumerateArray()
                .Where(release => release.TryGetProperty("sdk", out _))
                .Select(release => release.GetProperty("sdk"))
                .Where(s => s.TryGetProperty("version", out var v) && v.GetString() is { } text && text.StartsWith("10.") && !text.Contains('-'))
                .SelectMany(s => s.GetProperty("files").EnumerateArray().Select(f => new { Version = s.GetProperty("version").GetString()!, File = f.Clone() }))
                .Where(x => x.File.GetProperty("rid").GetString() == rid && x.File.GetProperty("name").GetString()!.EndsWith(extension, StringComparison.Ordinal))
                .OrderByDescending(x => Version.Parse(x.Version))
                .ToArray();
            if (sdkFiles.Length == 0) return Fail($"В официальном каталоге нет .NET SDK для {rid}.");
            var selected = sdkFiles[0].File;
            var url = selected.GetProperty("url").GetString()!;
            var hash = selected.GetProperty("hash").GetString()!;
            if (!AllowedSdkUrl(url) || hash.Length != 128) return Fail("Недопустимая ссылка или SHA-512 .NET SDK.");
            var target = Path.Combine(CacheRoot, "dotnet-10", rid);
            await InstallVerifiedArchive(http, url, hash, HashAlgorithmName.SHA512, target, extension == ".zip", false, ct);
            var executable = Path.Combine(target, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (!File.Exists(executable)) return Fail("Архив .NET SDK не содержит исполняемый файл dotnet.");
            MakeExecutable(executable);
            AddToPath(target, true);
            Console.WriteLine("[Prerequisites] .NET SDK установлен в: " + target);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or CryptographicException or OperationCanceledException or UnauthorizedAccessException or PlatformNotSupportedException)
        { return Fail("Не удалось подготовить .NET SDK: " + ex.Message); }
    }

    private static async Task<bool> InstallNodeAsync(CancellationToken ct)
    {
        try
        {
            Console.WriteLine("[Prerequisites] Загрузка официального Node.js LTS + npm...");
            using var http = CreateHttp();
            using var index = JsonDocument.Parse(await http.GetStringAsync("https://nodejs.org/dist/index.json", ct));
            var latest = index.RootElement.EnumerateArray()
                .FirstOrDefault(v => v.TryGetProperty("lts", out var lts) && lts.ValueKind == JsonValueKind.String &&
                    Version.TryParse(v.GetProperty("version").GetString()!.TrimStart('v'), out var parsed) && parsed.Major >= 22);
            if (latest.ValueKind == JsonValueKind.Undefined) return Fail("В каталоге Node.js не найден поддерживаемый LTS-релиз.");
            var version = latest.GetProperty("version").GetString()!;
            var rid = GetRid();
            var file = $"node-{version}-{rid}" + (OperatingSystem.IsWindows() ? ".zip" : ".tar.gz");
            var baseUrl = $"https://nodejs.org/dist/{version}/";
            var sums = await http.GetStringAsync(baseUrl + "SHASUMS256.txt", ct);
            var expected = sums.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Where(parts => parts.Length == 2 && string.Equals(parts[1], file, StringComparison.Ordinal))
                .Select(parts => parts[0]).SingleOrDefault();
            if (expected is null || expected.Length != 64) return Fail("Не удалось проверить SHA-256 архива Node.js " + file);
            var target = Path.Combine(CacheRoot, "node-lts", rid);
            await InstallVerifiedArchive(http, baseUrl + file, expected, HashAlgorithmName.SHA256, target, OperatingSystem.IsWindows(), true, ct);
            var bin = OperatingSystem.IsWindows() ? target : Path.Combine(target, "bin");
            var nodeExe = Path.Combine(bin, OperatingSystem.IsWindows() ? "node.exe" : "node");
            if (!File.Exists(nodeExe)) return Fail("Архив Node.js не содержит исполняемый файл node.");
            MakeExecutable(nodeExe);
            AddToPath(bin);
            Console.WriteLine("[Prerequisites] Node.js/npm установлены в: " + target);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or FormatException or CryptographicException or OperationCanceledException or UnauthorizedAccessException or PlatformNotSupportedException)
        { return Fail("Не удалось подготовить Node.js/npm: " + ex.Message); }
    }

    private static bool AllowedSdkUrl(string raw)
    {
        if (!Uri.TryCreate(raw, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length != 0) return false;
        return uri.Host.Equals("builds.dotnet.microsoft.com", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("dotnetcli.blob.core.windows.net", StringComparison.OrdinalIgnoreCase) ||
               uri.Host.Equals("download.visualstudio.microsoft.com", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false };
        return new HttpClient(handler) { Timeout = DownloadTimeout };
    }

    private static async Task InstallVerifiedArchive(HttpClient http, string url, string hash, HashAlgorithmName algorithm,
        string target, bool zip, bool flattenSingleFolder, CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidOperationException("Bootstrap download requires HTTPS.");
        var tempDir = Path.Combine(Path.GetTempPath(), "mysticego-prereq-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var archive = Path.Combine(tempDir, zip ? "package.zip" : "package.tar.gz");
            using (var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > MaxArchiveBytes) throw new InvalidOperationException("Archive size limit exceeded.");
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var file = File.Create(archive);
                var buffer = new byte[128 * 1024];
                long total = 0;
                int count;
                while ((count = await stream.ReadAsync(buffer, ct)) != 0)
                {
                    total += count;
                    if (total > MaxArchiveBytes) throw new InvalidOperationException("Archive size limit exceeded.");
                    await file.WriteAsync(buffer.AsMemory(0, count), ct);
                }
            }
            byte[] calculated;
            await using (var file = File.OpenRead(archive))
                calculated = algorithm == HashAlgorithmName.SHA512 ? await SHA512.HashDataAsync(file, ct) : await SHA256.HashDataAsync(file, ct);
            if (!CryptographicOperations.FixedTimeEquals(calculated, Convert.FromHexString(hash)))
                throw new CryptographicException("Archive checksum mismatch. Aborting.");
            var extracted = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extracted);
            if (zip) ZipFile.ExtractToDirectory(archive, extracted);
            else
            {
                await using var input = File.OpenRead(archive);
                await using var gzip = new GZipStream(input, CompressionMode.Decompress);
                await TarFile.ExtractToDirectoryAsync(gzip, extracted, overwriteFiles: false, cancellationToken: ct);
            }
            var installFrom = extracted;
            if (flattenSingleFolder)
            {
                var folders = Directory.GetDirectories(extracted);
                if (folders.Length != 1 || Directory.GetFiles(extracted).Length != 0)
                    throw new InvalidOperationException("Unexpected Node.js archive layout.");
                installFrom = folders[0];
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (Directory.Exists(target)) Directory.Move(target, target + ".old-" + Guid.NewGuid().ToString("N"));
            Directory.Move(installFrom, target);
        }
        finally { try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { } }
    }

    private static void MakeExecutable(string path)
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                   UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }

    private static async Task<(int ExitCode, string Stdout)> CaptureAsync(string command, string[] args, CancellationToken ct)
    {
        try
        {
            var start = new ProcessStartInfo(OperatingSystem.IsWindows() && command == "npm" ? "cmd.exe" : command)
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            if (OperatingSystem.IsWindows() && command == "npm")
            {
                start.ArgumentList.Add("/d"); start.ArgumentList.Add("/c"); start.ArgumentList.Add("npm");
            }
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            using var process = Process.Start(start);
            if (process is null) return (-1, "");
            var stdOut = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stdErr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            await stdErr;
            return (process.ExitCode, await stdOut);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or OperationCanceledException or IOException)
        { return (-1, ""); }
    }

    private static bool Fail(string error) { Console.Error.WriteLine("[Prerequisites] " + error); return false; }
}
