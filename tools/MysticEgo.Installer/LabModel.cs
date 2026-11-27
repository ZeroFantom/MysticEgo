using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MysticEgo.Deployment;

internal static class LabModel
{
    internal static async Task<string> Setup(CancellationToken ct, string role)
    {
        Console.WriteLine("LM Studio: проверяю локальный сервер.");
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        // Preserve API authentication for an already configured local LM Studio.
        var token=Environment.GetEnvironmentVariable("MYSTICEGO_LMSTUDIO_TOKEN");
        if(!string.IsNullOrWhiteSpace(token))
            http.DefaultRequestHeaders.Authorization=new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer",token);
        var catalog = await Probe(http, ct);
        var lms = FindCli();
        if (catalog is null)
        {
            if(OperatingSystem.IsLinux())await EnsureLinuxRuntime(ct);
            var hadCli = lms is not null;
            lms ??= await Install(ct);
            PrepareTemporaryDirectory();
            Console.WriteLine("LM Studio: запускаю службу и сервер на 127.0.0.1:1234.");
            try { await StartDaemon(lms, ct); }
            catch (IOException) when (hadCli && OperatingSystem.IsWindows())
            {
                lms = await Install(ct);
                await StartDaemon(lms, ct);
            }
            catalog = await StartServer(http, lms, ct);
        }
        else PrepareTemporaryDirectory();

        var preference = LocalModels.Preference(role);
        var primary = preference[0];
        var installed = preference.SelectMany(name => catalog.Where(model => LocalModels.Matches(model.Key, name)))
            .DistinctBy(model => model.Key, StringComparer.OrdinalIgnoreCase).ToArray();
        Exception? lastError = null;

        var equivalent = primary == LocalModels.LargeModel ? LocalModels.LargeAlternative : primary;
        if (!installed.Any(model => LocalModels.Matches(model.Key, primary) || LocalModels.Matches(model.Key, equivalent)))
        {
            try
            {
                lms ??= await Install(ct);
                Console.WriteLine($"LM Studio: скачиваю {primary} (Q4_K_M).");
                using var download = CancellationTokenSource.CreateLinkedTokenSource(ct);
                download.CancelAfter(TimeSpan.FromMinutes(45));
                try { await LabPackage.RunTool(lms, ["get", primary + "@q4_k_m", "--gguf", "--yes"], Environment.CurrentDirectory, download.Token); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Истекло время скачивания модели."); }
                var updated = await Probe(http, ct);
                var downloaded = updated?.FirstOrDefault(model => LocalModels.Matches(model.Key, primary));
                installed = [downloaded ?? new LocalModels.Installed(primary, []), ..installed];
            }
            catch (Exception error) when (installed.Length != 0 && !ct.IsCancellationRequested && error is IOException or HttpRequestException)
            {
                lastError = error;
                Console.WriteLine($"LM Studio: скачивание {primary} завершилось ошибкой. Проверяю скачанные модели. {error.Message}");
            }
        }

        foreach (var model in installed)
        {
            var context = LocalModels.Matches(model.Key, LocalModels.SmallModel) ? 4096 :
                LocalModels.Matches(model.Key, LocalModels.MediumModel) ? 6144 : 8192;
            try
            {
                var id = model.Instances.FirstOrDefault() ?? await Load(http, lms, model.Key, context, ct);
                await Verify(http, id, model.Key, ct);
                return id;
            }
            catch (IOException error)
            {
                lastError = error;
                Console.WriteLine($"LM Studio: {model.Key}. {error.Message}");
            }
        }
        throw new IOException(lastError?.Message ?? "LM Studio не удалось подготовить языковую модель.", lastError);
    }

    private static string DefaultHome() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio");

    private static string StudioHome()
    {
        var pointer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".lmstudio-home-pointer");
        var configured = File.Exists(pointer) ? Environment.ExpandEnvironmentVariables(File.ReadAllText(pointer).Trim()) : "";
        return Path.IsPathFullyQualified(configured) ? configured : DefaultHome();
    }

    private static void PrepareTemporaryDirectory()
    {
        var preferred = StudioHome();
        foreach (var home in new[] { preferred, DefaultHome() }.Distinct())
        {
            // The Desktop server and llmster may resolve their home before the CLI reads its pointer.
            if (home != preferred && !Directory.Exists(home)) continue;
            var temp = Path.Combine(home, ".internal", "temp");
            if (!Directory.Exists(temp))
            {
                Directory.CreateDirectory(temp);
                Console.WriteLine("LM Studio: подготовлен каталог временных файлов.");
            }
        }
    }

    private static string? FindCli()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var name = OperatingSystem.IsWindows() ? "lms.exe" : "lms";
        return new[] { Path.Combine(StudioHome(), "bin"), Path.Combine(DefaultHome(), "bin"), Path.Combine(user, ".cache", "lm-studio", "bin") }
            .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            .Where(Path.IsPathFullyQualified).Select(path => Path.Combine(path, name)).FirstOrDefault(File.Exists);
    }

    /// <summary>Load-check libatomic before llmster, asking the desktop privilege broker only if needed.
    /// Never launch the model daemon or downloaded installer as root.</summary>
    private static async Task EnsureLinuxRuntime(CancellationToken ct)
    {
        if(System.Runtime.InteropServices.NativeLibrary.TryLoad("libatomic.so.1",out var handle))
        {System.Runtime.InteropServices.NativeLibrary.Free(handle);return;}
        if(!File.Exists("/usr/bin/apt-get")||!File.Exists("/usr/bin/pkexec"))
            throw new IOException("LM Studio requires libatomic.so.1. Automatic dependency installation requires Ubuntu/Debian apt and a graphical PolicyKit agent.");
        if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))&&string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
            throw new IOException("Missing libatomic1. Open the desktop installer in a graphical session to authorize dependency installation.");
        Console.WriteLine("LM Studio: подтвердите системную установку libatomic1 в диалоге PolicyKit.");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(5));
        await LabPackage.RunTool("/usr/bin/pkexec",["/usr/bin/apt-get","-o","DPkg::Lock::Timeout=120","install","-y","--no-install-recommends","libatomic1"],Environment.CurrentDirectory,deadline.Token);
        if(!System.Runtime.InteropServices.NativeLibrary.TryLoad("libatomic.so.1",out handle))
            throw new IOException("libatomic1 installed but cannot be loaded.");
        System.Runtime.InteropServices.NativeLibrary.Free(handle);
    }

    private static async Task<string> Install(CancellationToken ct)
    {
        if (OperatingSystem.IsWindows())
        {
            if(System.Runtime.InteropServices.RuntimeInformation.OSArchitecture!=System.Runtime.InteropServices.Architecture.X64)
                throw new IOException("Автоматическая установка LM Studio использует проверенный архив Windows x64.");
            await WindowsModelService.Install(ct);
        }
        else if (OperatingSystem.IsLinux())
        {
            await EnsureLinuxRuntime(ct);
            Console.WriteLine("LM Studio: скачиваю и устанавливаю службу llmster.");
            var script = Path.Combine(Path.GetTempPath(), "mysticego-llmster-" + Guid.NewGuid().ToString("N") + ".sh");
            try
            {
                using var download = new HttpClient();
                var bytes = await download.GetByteArrayAsync("https://lmstudio.ai/install.sh", ct);
                await File.WriteAllBytesAsync(script, bytes, ct);
                await LabPackage.RunTool("/bin/bash", [script, "--no-modify-path"], Environment.CurrentDirectory, ct);
            }
            finally { File.Delete(script); }
        }
        else throw new IOException("Автоматическая установка LM Studio доступна на Windows и Linux.");
        return FindCli() ?? throw new IOException("Установка завершилась без команды lms.");
    }

    private static async Task StartDaemon(string lms, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try { await LabPackage.RunTool(lms, ["daemon", "up"], Environment.CurrentDirectory, timeout.Token); }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Служба LM Studio не запустилась за 2 минуты."); }
    }

    private static async Task<LocalModels.Installed[]> StartServer(HttpClient http, string lms, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            PrepareTemporaryDirectory();
            try { await LabPackage.RunTool(lms, ["server", "start", "--port", "1234", "--bind", "127.0.0.1"], Environment.CurrentDirectory, deadline.Token); }
            catch (IOException)
            {
                if (await Probe(http, deadline.Token) is { } running) return running;
                throw;
            }
            while (true)
            {
                if (await Probe(http, deadline.Token) is { } running) return running;
                await Task.Delay(TimeSpan.FromSeconds(2), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("LM Studio не запустил API за 2 минуты."); }
    }

    private static async Task<LocalModels.Installed[]?> Probe(HttpClient http, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        try { return await LocalModels.Catalog(http, timeout.Token); }
        catch (HttpRequestException error) when (error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        { throw new IOException("Локальный API LM Studio запрашивает токен.", error); }
        catch (HttpRequestException error) when (error.StatusCode is null || (int)error.StatusCode >= 500) { return null; }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return null; }
    }

    private static async Task<string> Load(HttpClient http, string? lms, string key, int context, CancellationToken ct)
    {
        Console.WriteLine($"LM Studio: загружаю в память {key}.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        try
        {
            using var response = await http.PostAsJsonAsync(LocalModels.Address + "/api/v1/models/load", new { model = key, context_length = context }, timeout.Token);
            if (LocalModels.Unsupported(response.StatusCode))
            {
                if (lms is null) throw new IOException("Текущая версия LM Studio требует команду lms для загрузки модели.");
                await LabPackage.RunTool(lms, ["load", key, "--identifier", LocalModels.Alias, "--context-length", context.ToString()], Environment.CurrentDirectory, timeout.Token);
                return LocalModels.Alias;
            }
            if (!response.IsSuccessStatusCode) throw await LocalModelChat.Error(response, timeout.Token);
            var loaded = await response.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
            return LocalModels.Text(loaded, "instance_id") ?? throw new IOException("LM Studio не вернул идентификатор загруженной модели.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Истекло время загрузки модели в память."); }
    }

    private static async Task Verify(HttpClient http, string id, string key, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        try
        {
            await LocalModelChat.Respond(http, id, "Reply with OK. /no_think", [new("user", "OK")], 256, 0, null, timeout.Token, key);
            Console.WriteLine($"LM Studio: проверен ответ модели {id}.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new IOException("Истекло время контрольного запроса к модели."); }
    }

}
