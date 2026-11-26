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
            if (embeddedAudit && !path.StartsWith("/og/", StringComparison.Ordinal))
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
