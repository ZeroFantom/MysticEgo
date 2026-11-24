using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using MysticEgo.Deployment;
using MysticEgo.Distributed;

internal sealed class AgentStationUpdates(string location, StationEndpoints endpoints)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private string Folder => Path.Combine(location, "agent-revisions");
    private string ActiveFile => Path.Combine(Folder, "current.json");
    private string PendingFile => Path.Combine(location, "agent", "update-staging", "pending.json");
    private string FailedFile => Path.Combine(Folder, "rejected.json");
    private static readonly Version Bundled = Version.Parse("0.8.0");
    public Release? Active => Read(ActiveFile);
    internal sealed record Release(string Version, string Folder, string Sha256);
    private static Release? Read(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<Release>(File.ReadAllText(file), Json) : null; }
        catch (JsonException) { return null; }
    }

    public async Task<Release?> Prepare(CancellationToken ct)
    {
        if (!File.Exists(PendingFile)) return null;
        PendingAgentRelease? pending;
        try { pending = JsonSerializer.Deserialize<PendingAgentRelease>(await File.ReadAllTextAsync(PendingFile, ct), Json); }
        finally { File.Delete(PendingFile); }
        if (pending is null) return null;
        var release = pending.Release;
        var publicKey = pending.Source switch { "local" => endpoints.LocalReleaseKey, "global" => endpoints.GlobalReleaseKey, _ => null };
        if (string.IsNullOrWhiteSpace(publicKey) || !AgentReleaseEndpoints.Verify(release, publicKey)) return null;
        var version = Version.Parse(release.Version);
        var old = Active;
        if (version <= (old is null ? Bundled : Version.Parse(old.Version)) || Read(FailedFile)?.Sha256 == release.Sha256) return null;
        var staged = Path.Combine(Path.GetDirectoryName(PendingFile)!, "agent-" + release.Version + ".zip");
        if (!File.Exists(staged) || new FileInfo(staged).Length != release.Size) return null;
        await using (var stream = File.OpenRead(staged))
            if (!string.Equals(Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)), release.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Не совпала контрольная сумма релиза Agent.");
        Directory.CreateDirectory(Folder);
        var destination = Path.Combine(Folder, release.Version + "-" + release.Sha256[..10]);
        if (Directory.Exists(destination))
        {
            if (File.Exists(Path.Combine(destination, "Api.Agent.dll"))) return new(release.Version, destination, release.Sha256);
            Directory.Delete(destination, true);
        }
        var temporary = destination + ".tmp";
        if (Directory.Exists(temporary)) Directory.Delete(temporary, true);
        try
        {
            using var archive = ZipFile.OpenRead(staged);
            if (archive.Entries.Count > 2000 || archive.Entries.Sum(entry => entry.Length) > 500_000_000)
                throw new InvalidDataException("Пакет обновления слишком велик.");
            archive.ExtractToDirectory(temporary);
            foreach (var required in new[] { "Api.Agent.dll", "Api.Agent.runtimeconfig.json", "Api.Agent.deps.json" })
                if (!File.Exists(Path.Combine(temporary, required))) throw new InvalidDataException("Релиз Agent должен содержать полный publish.");
            Directory.Move(temporary, destination);
            return new(release.Version, destination, release.Sha256);
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }

    public void Saved(Release release) => Write(ActiveFile, release);
    public void Rejected(Release release) => Write(FailedFile, release);
    private static void Write(string path, Release release)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(release, Json));
        File.Move(path + ".tmp", path, true);
    }
}
