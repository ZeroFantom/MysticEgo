using System.IO.Compression;
using System.Security.Cryptography;

internal static class WindowsModelService
{
    // The release and digest are pinned together. Only the official x64 archive is executed.
    private const string Url = "https://llmster.lmstudio.ai/download/0.1.1-2-win32-x64.full.zip";
    private const string Digest = "389324b7bd4037faec7c817651413c90490b0d4edda029af2ce81c9f5b217e537ecfe19e761d49757aefbe4c5e3c3ba68e4537bb5a7b1f2d3305cfdf8b720844";

    internal static async Task Install(CancellationToken ct)
    {
        var dir = Directory.CreateTempSubdirectory("mysticego-llmster-");
        var archive = Path.Combine(dir.FullName, "service.zip");
        try
        {
            Console.WriteLine("Загрузка фоновой службы LM Studio для Windows x64…");
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MysticEgo/1.0");
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromMinutes(30));
            using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            response.EnsureSuccessStatusCode();
            await using (var file = File.Create(archive))
            {
                await response.Content.CopyToAsync(file, deadline.Token);
                file.Position = 0;
                var hash = Convert.ToHexString(await SHA512.HashDataAsync(file, deadline.Token));
                if (!hash.Equals(Digest, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Контрольная сумма службы LM Studio не совпала.");
            }
            var release = Path.Combine(dir.FullName, "release");
            ZipFile.ExtractToDirectory(archive, release);
            await LabPackage.RunTool(Path.Combine(release, "llmster.exe"), ["bootstrap"], release, ct,
                environment: new Dictionary<string,string>{{"LMS_BOOTSTRAP_INSTALL_SH","1"},{"LMS_NO_MODIFY_PATH","1"}});
        }
        finally
        {
            try { dir.Delete(true); }
            catch (IOException) { Console.Error.WriteLine("Временные файлы LM Studio: " + dir.FullName); }
        }
    }
}
