using System.IO.Compression;
using System.Text.Json.Nodes;

sealed partial class CloudDesk
{
    async Task<object> ProvisionInfrastructure(JsonObject args, Action<string> progress)
    {
        if (S(args, "confirm") != "PROVISION_INFRASTRUCTURE" || S(args, "user") != "root")
            throw new Exception("Подтвердите установку. Нужен доверенный SSH root.");

        await EnsureKey();
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var fields = new JsonObject();
        foreach (var name in new[] { "component", "url", "bind", "cidr", "role", "secret" })
            fields[name] = S(args, name);
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var name in new[] { "install.sh", "install.py", "run-web.sh" })
                archive.CreateEntryFromFile(Path.Combine(root, "ops", "infrastructure", name), name);
            var monitoring = Path.Combine(root, "ops", "monitoring");
            foreach (var file in Directory.EnumerateFiles(monitoring, "*", SearchOption.AllDirectories))
                archive.CreateEntryFromFile(file, "monitoring/" + Path.GetRelativePath(monitoring, file).Replace('\\', '/'));
            using var request = new StreamWriter(archive.CreateEntry("request.json").Open());
            request.Write(fields.ToJsonString());
        }
        const string install = "set -eu; umask 077; d=$(mktemp -d); trap 'rm -rf -- \"$d\"' EXIT; " +
            "base64 -d > \"$d/bundle.zip\"; python3 -m zipfile -e \"$d/bundle.zip\" \"$d\"; bash \"$d/install.sh\"";
        progress("Передаём проверенный комплект установки по SSH");
        string Clean(string line) { var value=S(args,"secret"); return value.Length==0?Redact(line):Redact(line).Replace(value,"[REDACTED]",StringComparison.Ordinal); }
        var output = await Exec("ssh", [..SshArgs(args), "bash -c " + ShellQuote(install)],
            input: Convert.ToBase64String(bytes.ToArray()), minutes: 45, onOutput: line => progress(Clean(line)));
        return new { output = Clean(output) };
    }
}
