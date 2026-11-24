using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

sealed partial class CloudDesk
{
    async Task<object> DeployAuditPortal(JsonObject args, Action<string> progress)
    {
        if (S(args, "confirm") != "DEPLOY_AUDIT")
            throw new Exception("Подтвердите развёртывание веб-панели Audit.");
        if (S(args, "user") != "root")
            throw new Exception("Для установки Audit нужен доверенный SSH root.");
        Target(args);
        Port(args);

        var mode = S(args, "mode");
        if (mode is not ("embedded" or "site"))
            throw new Exception("Выберите способ размещения Audit.");
        var origin = new Uri(TopologyOrigin(S(args, "origin")));
        var domain = mode == "embedded" ? origin.Host : S(args, "domain").ToLowerInvariant();
        if (!origin.IsDefaultPort || !Regex.IsMatch(origin.Host, DnsNamePattern) ||
            domain.Length > 253 || !Regex.IsMatch(domain, DnsNamePattern) ||
            (mode == "site" && domain == origin.Host))
            throw new Exception("Укажите HTTPS-домен Orchestra и отдельный DNS-домен для сайта Audit.");

        progress("Проверяем права главного разработчика в Orchestra");
        var node = await RequireDeveloper(origin.GetLeftPart(UriPartial.Authority), ownerOnly: true);

        var solution = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        var installer = Path.Combine(solution, "ops", "audit-portal", "install-audit-portal.sh");
        var web = Path.Combine(solution, "runtime", "app", "web", "audit");
        if (!File.Exists(Path.Combine(web, "index.html")))
            web = Path.Combine(solution, "frontend", "apps", "audit", "dist");
        if (!File.Exists(installer) || !File.Exists(Path.Combine(web, "index.html")))
            throw new Exception("Распакуйте полный релиз MysticEgo с собранной панелью Audit.");

        progress("Проверяем сервер и зависимости по SSH");
        var preflight = "set -eu; test \"$(id -u)\" = 0; for tool in bash curl unzip sha256sum python3 flock; do " +
            "command -v \"$tool\" >/dev/null || { echo \"Не найдено: $tool\" >&2; exit 1; }; done";
        if (mode == "embedded")
            preflight += "; systemctl is-active --quiet mysticego-orchestra.service";
        else
            preflight += "; command -v caddy >/dev/null && systemctl is-active --quiet caddy.service || { echo 'Установите и запустите Caddy на выбранном VPS.' >&2; exit 1; }";
        await SSH(args, preflight, 1);

        var local = Directory.CreateTempSubdirectory("mysticego-audit-");
        var remote = "/root/.clouddesk-audit-" + Guid.NewGuid().ToString("N");
        try
        {
            var zip = Path.Combine(local.FullName, "audit.zip");
            using (var package = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                package.CreateEntryFromFile(installer, "install-audit-portal.sh");
                foreach (var file in Directory.EnumerateFiles(web, "*", SearchOption.AllDirectories))
                    package.CreateEntryFromFile(file, "web/" + Path.GetRelativePath(web, file).Replace('\\', '/'));
            }
            string digest;
            using (var stream = File.OpenRead(zip))
                digest = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream));

            await SSH(args, "umask 077; mkdir -m 700 " + remote, 1);
            try
            {
                progress("Передаём веб-панель Audit через SCP");
                await Upload(args, zip, remote + "/audit.zip");
                progress("Проверяем SHA-256 и запускаем установщик");
                var options = mode == "embedded"
                    ? "--same-orchestra " + ShellQuote(node)
                    : "--site " + ShellQuote(domain) + " " + ShellQuote(origin.Host) + " " + ShellQuote(node);
                var command = "cd " + remote + " && printf '%s  audit.zip\\n' " + digest +
                    " | sha256sum -c - && unzip -q audit.zip && bash install-audit-portal.sh " + options;
                var output = await SSH(args, "flock -n /run/lock/mysticego-clouddesk-deploy.lock bash -c " +
                    ShellQuote(command), 8, message => progress(Redact(message)));
                return new { url = $"https://{domain}/audit-portal/", output };
            }
            finally
            {
                try { await SSH(args, "rm -rf -- " + remote, 1); } catch { }
            }
        }
        finally { local.Delete(true); }
    }
}
