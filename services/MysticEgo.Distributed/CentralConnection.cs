using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MysticEgo.Distributed;

public static class CentralConnection
{
    public static void UseCentralConnection(this WebApplication app, NodeOptions node)
    {
        if (node.Kind == NodeKind.Orchestra || !Uri.TryCreate(node.OrchestraUrl, UriKind.Absolute, out var central)) return;

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var personal = path.StartsWithSegments("/api/v1/collective") || path.StartsWithSegments("/api/v1/faq");
            var avatar = node.Kind == NodeKind.AuditOrchestra && node.Managed && path.StartsWithSegments("/api/v1/avatars/account");
            var managed = path.StartsWithSegments("/api/v1/managed");
            var bearer = context.Request.Headers.Authorization.ToString();
            var human = context.User.Identity?.IsAuthenticated == true && !context.User.HasClaim("token_use", "device");

            if (!personal && !managed && !avatar)
            {
                if (human && node.Managed && node.Kind is NodeKind.Global or NodeKind.Local &&
                    !EcosystemHierarchy.IsDeveloper(context.User) && (path.StartsWithSegments("/api/v1") || path.Equals("/mcp")))
                    await AddDelegatedRoles(context, central, node, bearer);
                await next(context);
                return;
            }

            if (!human || !AuthenticationHeaderValue.TryParse(bearer, out var authorization))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Only the configured Orchestra receives requests for the shared account data.
            using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method),
                central.AbsoluteUri.TrimEnd('/') + path.ToUriComponent() + context.Request.QueryString.ToUriComponent());
            request.Headers.Authorization = authorization;
            if (managed && node.Managed && node.Kind is NodeKind.Global or NodeKind.Local)
            {
                try
                {
                    var identity = context.RequestServices.GetRequiredService<IdentityTokenSource>();
                    request.Headers.TryAddWithoutValidation("X-Mystic-Parent", await identity.GetAsync(context.RequestAborted));
                }
                catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    return;
                }
            }
            if (HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
            {
                var sticker = path.Equals("/api/v1/collective/stickers", StringComparison.OrdinalIgnoreCase);
                var voiceUpload = HttpMethods.IsPost(context.Request.Method) &&
                    path.StartsWithSegments("/api/v1/collective/voice", out var voicePath) &&
                    Guid.TryParse(voicePath.Value?.Trim('/'), out _);
                var avatarUpload = avatar && path.Equals("/api/v1/avatars/account/asset", StringComparison.OrdinalIgnoreCase);
                var maximum = avatarUpload ? 25 * 1024 * 1024 : voiceUpload ? 5 * 1024 * 1024 : sticker ? 524_288 : 65_536;
                if (context.Request.ContentLength > maximum)
                {
                    context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                    return;
                }
                var type = new MediaTypeHeaderValue(sticker || voiceUpload ? "application/octet-stream" : "application/json");
                if (avatarUpload && (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out type) || type.MediaType != "multipart/form-data"))
                {
                    context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                    return;
                }
                request.Content = new StreamContent(context.Request.Body);
                request.Content.Headers.ContentType = type;
            }
            try
            {
                using var client = CreateClient(context);
                using var result = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);
                context.Response.StatusCode = (int)result.StatusCode;
                context.Response.ContentType = result.Content.Headers.ContentType?.ToString();
                if (result.Content.Headers.ContentDisposition is { } disposition)
                    context.Response.Headers.ContentDisposition = disposition.ToString();
                context.Response.Headers.CacheControl = "no-store";
                await result.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
            {
                if (!context.Response.HasStarted)
                {
                    context.Response.StatusCode = StatusCodes.Status502BadGateway;
                    await context.Response.WriteAsJsonAsync(new { error = "Оркестр недоступен. Проверьте адрес и подключение." });
                }
            }
        });
    }

    private static HttpClient CreateClient(HttpContext context) =>
        context.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("mysticego-logged-api");

    private static async Task AddDelegatedRoles(HttpContext context, Uri central, NodeOptions node, string bearer)
    {
        if (!AuthenticationHeaderValue.TryParse(bearer, out var authorization) || !Guid.TryParse(node.IdentityId, out var id)) return;
        using var client = CreateClient(context);
        using var request = new HttpRequestMessage(HttpMethod.Get, central.AbsoluteUri.TrimEnd('/') + $"/api/v1/managed/access/{id}");
        request.Headers.Authorization = authorization;
        try
        {
            using var response = await client.SendAsync(request, context.RequestAborted);
            if (!response.IsSuccessStatusCode) return;
            var access = await response.Content.ReadFromJsonAsync<DelegatedAccess>(context.RequestAborted);
            if (access?.CanRead != true) return;
            var claims = new List<Claim> { new("permission", "node.read") };
            if (access.CanManage)
            {
                claims.Add(new("permission", "node.write"));
                claims.Add(new("permission", "node.command"));
                claims.Add(new(ClaimTypes.Role, node.Kind == NodeKind.Global ? "global.manager" : "local.admin"));
            }
            context.User.AddIdentity(new ClaimsIdentity(claims));
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("DelegatedAccess")
                .LogWarning("Could not verify organization access for node {NodeId}", id);
        }
    }

    private sealed record DelegatedAccess(bool CanRead, bool CanManage);
}
