using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

/// <summary>Public, role-only HTML metadata. Never trusts request Host or forwarded headers.</summary>
public static class PanelSocialPreviews
{
    private const string Start = "<!-- mysticego-social-start -->";
    private const string End = "<!-- mysticego-social-end -->";
    // Only known UI routes receive the role-level preview; never reflect private IDs,
    // query parameters, cookies, forwarded headers, or arbitrary request paths.
    private static readonly HashSet<string> PanelRoutes = new(StringComparer.OrdinalIgnoreCase)
    {
        "/profile", "/account-security", "/collective", "/organizations", "/nodes",
        "/setup", "/commands", "/automation", "/faq", "/audit", "/diagnostics",
        "/settings", "/notifications", "/agents", "/assistant", "/avatars",
        "/system", "/software", "/identities", "/users", "/updates", "/presets",
        "/deploy-native", "/development-team", "/development-workspace",
        "/model-workers", "/clouddesk-downloads", "/panels", "/project-branches",
        "/developer-assistant", "/orchestra-update", "/infrastructure/services",
        "/infrastructure/ai", "/infrastructure/database", "/infrastructure/metrics",
        "/infrastructure/capacity", "/infrastructure/self-tests",
        "/infrastructure/graphs/macro", "/infrastructure/graphs/micro"
    };
    private static bool KnownRoute(string path) => PanelRoutes.Contains(path) ||
        (path.StartsWith("/agents/", StringComparison.OrdinalIgnoreCase) &&
         Guid.TryParse(path["/agents/".Length..], out _));

    public static string Render(string html, string? publicUrl, bool embeddedAudit = false)
    {
        var start = html.IndexOf(Start, StringComparison.Ordinal);
        var end = html.IndexOf(End, StringComparison.Ordinal);
        if (start < 0 || end <= start) return html;
        end += End.Length;
        var origin = Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp) &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) ? uri.GetLeftPart(UriPartial.Authority) : "";
        var block = Regex.Replace(html[start..end], "(?<attr>content|href)=\"(?<path>/[a-zA-Z0-9/_.-]*)\"", match =>
        {
            var path = match.Groups["path"].Value;
            if (embeddedAudit)
                path = "/audit-portal" + path;
            return match.Groups["attr"].Value + "=\"" + HtmlEncoder.Default.Encode(origin + path) + "\"";
        });
        // The secure image property is only meaningful for an HTTPS origin.
        if (origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            block = Regex.Replace(block, "(<meta property=\"og:image\" content=\"([^\"]+)\"/>)",
                "$1\n<meta property=\"og:image:secure_url\" content=\"$2\"/>");
        }
        return html[..start] + block + html[end..];
    }

    public static void UsePanelSocialPreviews(this WebApplication app)
    {
        var node = app.Services.GetRequiredService<NodeOptions>();
        app.Use(async (context, next) =>
        {
            if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method))
            { await next(context); return; }

            var path = context.Request.Path.Value?.TrimEnd('/') ?? "";
            if(path is "/robots.txt" or "/sitemap.xml")
            {
                var valid=Uri.TryCreate(node.PublicUrl,UriKind.Absolute,out var uri)&&uri.Scheme=="https"&&
                    string.IsNullOrEmpty(uri.UserInfo)&&string.IsNullOrEmpty(uri.Query)&&string.IsNullOrEmpty(uri.Fragment);
                var origin=valid?uri!.GetLeftPart(UriPartial.Authority):"";
                var indexable=node.Kind==NodeKind.Orchestra&&valid;
                var pages=new[]{"/studio","/developer","/desktop-downloads"};
                if(path=="/sitemap.xml"&&!indexable){context.Response.StatusCode=404;return;}
                var content=path=="/robots.txt"?
                    "User-agent: *\nDisallow: /api/\nDisallow: /hub/\nDisallow: /mcp/\n"+(indexable?"Sitemap: "+origin+"/sitemap.xml\n":""):
                    "<?xml version=\"1.0\" encoding=\"UTF-8\"?><urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">"+
                    string.Concat(pages.Select(p=>"<url><loc>"+System.Security.SecurityElement.Escape(origin+p)+"</loc></url>"))+"</urlset>";
                context.Response.ContentType=path=="/robots.txt"?"text/plain; charset=utf-8":"application/xml; charset=utf-8";
                context.Response.Headers.CacheControl="public, max-age=300";
                if(!HttpMethods.IsHead(context.Request.Method))await context.Response.WriteAsync(content,context.RequestAborted);
                return;
            }
            // Keep the updater's existing local byte-for-byte health check intact.
            // This branch never constructs URLs from the Host header.
            if (path == "/audit-portal" && !context.Request.IsHttps &&
                context.Request.Host.Host == "127.0.0.1" &&
                context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) &&
                !context.Request.Headers.ContainsKey("X-Forwarded-Host") &&
                !context.Request.Headers.ContainsKey("X-Forwarded-Proto"))
            { await next(context); return; }

            var role = node.Kind.ToString().ToLowerInvariant();
            var web = app.Environment.WebRootPath;
            var embeddedCover = node.Kind == NodeKind.Orchestra && path == "/audit-portal/og/audit.png";
            var normalCover = path == "/og/" + role + ".png";
            // Cover assets are public, fixed paths. Return a real PNG or 404, never SPA HTML.
            if (embeddedCover || normalCover)
            {
                var coverRoot = embeddedCover ? app.Configuration["Managed:AuditWebRoot"] : web;
                var cover = coverRoot is null ? null : Path.Combine(coverRoot, "og", (embeddedCover ? "audit" : role) + ".png");
                if (cover is null || !File.Exists(cover))
                { context.Response.StatusCode = 404; return; }
                context.Response.ContentType = "image/png";
                context.Response.ContentLength = new FileInfo(cover).Length;
                context.Response.Headers.CacheControl = "public, max-age=300";
                context.Response.Headers["X-Content-Type-Options"] = "nosniff";
                if (!HttpMethods.IsHead(context.Request.Method))
                    await context.Response.SendFileAsync(cover, context.RequestAborted);
                return;
            }

            var audit = node.Kind == NodeKind.Orchestra &&
                (path == "/audit-portal" || path == "/audit-portal/share" ||
                 path == "/audit-portal/index.html" ||
                 (path.StartsWith("/audit-portal/", StringComparison.Ordinal) &&
                  KnownRoute(path["/audit-portal".Length..])));
            if (audit) web = app.Configuration["Managed:AuditWebRoot"];
            var relative = path switch
            {
                "" or "/index.html" or "/audit-portal" or "/audit-portal/index.html" => "index.html",
                "/share" or "/audit-portal/share" => "social/share.html",
                "/license" when node.Kind == NodeKind.Orchestra => "social/license.html",
                "/studio" when node.Kind == NodeKind.Orchestra => "social/studio.html",
                "/developer" when node.Kind == NodeKind.Orchestra => "social/developer.html",
                "/desktop-downloads" when node.Kind == NodeKind.Orchestra => "social/desktop-downloads.html",
                _ when KnownRoute(path) || (audit && KnownRoute(path["/audit-portal".Length..])) => "index.html",
                _ => null
            };
            if (relative is null || web is null || (path.StartsWith("/audit-portal", StringComparison.Ordinal) && !audit))
            {
                if(!path.StartsWith("/api/",StringComparison.OrdinalIgnoreCase)&&!path.StartsWith("/assets/",StringComparison.OrdinalIgnoreCase)&&!Path.HasExtension(path))
                    context.Response.Headers["X-Robots-Tag"]="noindex, follow";
                await next(context); return;
            }

            var file = Path.Combine(web, relative);
            if (!File.Exists(file)) { await next(context); return; }
            var html = await File.ReadAllTextAsync(file, context.RequestAborted);
            if (!html.Contains(Start, StringComparison.Ordinal)) { await next(context); return; }

            var rendered = Render(html, node.PublicUrl, audit);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "public, max-age=60";
            // Login/workspace content remains non-indexable, but the public cover is fetchable.
            if (relative == "index.html" || relative == "social/share.html")
                context.Response.Headers["X-Robots-Tag"] = "noindex, follow, max-image-preview:large";
            context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(rendered);
            if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.WriteAsync(rendered, context.RequestAborted);
        });
    }
}
