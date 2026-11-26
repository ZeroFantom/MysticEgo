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
            // Keep the updater's existing local byte-for-byte health check intact.
            // This branch never constructs URLs from the Host header.
            if (path == "/audit-portal" && !context.Request.IsHttps &&
                context.Request.Host.Host == "127.0.0.1" &&
                context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip) &&
                !context.Request.Headers.ContainsKey("X-Forwarded-Host") &&
                !context.Request.Headers.ContainsKey("X-Forwarded-Proto"))
            { await next(context); return; }

            if (node.Kind == NodeKind.Orchestra && path == "/audit-portal/og/audit.png")
            {
                var auditRoot = app.Configuration["Managed:AuditWebRoot"];
                var cover = auditRoot is null ? null : Path.Combine(auditRoot, "og", "audit.png");
                if (cover is null || !File.Exists(cover))
                { context.Response.StatusCode = 404; return; }
                context.Response.ContentType = "image/png";
                context.Response.ContentLength = new FileInfo(cover).Length;
                context.Response.Headers.CacheControl = "public, max-age=3600";
                if (!HttpMethods.IsHead(context.Request.Method))
                    await context.Response.SendFileAsync(cover, context.RequestAborted);
                return;
            }

            var web = app.Environment.WebRootPath;
            var audit = node.Kind == NodeKind.Orchestra &&
                (path == "/audit-portal" || path == "/audit-portal/share" || path == "/audit-portal/index.html");
            if (audit) web = app.Configuration["Managed:AuditWebRoot"];
            var relative = path switch
            {
                "" or "/index.html" or "/audit-portal" or "/audit-portal/index.html" => "index.html",
                "/share" or "/audit-portal/share" => "social/share.html",
                "/studio" when node.Kind == NodeKind.Orchestra => "social/studio.html",
                "/developer" when node.Kind == NodeKind.Orchestra => "social/developer.html",
                "/desktop-downloads" when node.Kind == NodeKind.Orchestra => "social/desktop-downloads.html",
                _ => null
            };
            if (relative is null || web is null || (path.StartsWith("/audit-portal", StringComparison.Ordinal) && !audit))
            { await next(context); return; }

            var file = Path.Combine(web, relative);
            if (!File.Exists(file)) { await next(context); return; }
            var html = await File.ReadAllTextAsync(file, context.RequestAborted);
            if (!html.Contains(Start, StringComparison.Ordinal)) { await next(context); return; }

            var rendered = Render(html, node.PublicUrl, audit);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers.CacheControl = "no-cache";
            if (HttpMethods.IsHead(context.Request.Method))
                context.Response.ContentLength = System.Text.Encoding.UTF8.GetByteCount(rendered);
            else await context.Response.WriteAsync(rendered, context.RequestAborted);
        });
    }
}
