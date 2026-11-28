using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

internal static class AuthGateway
{
    public static void MapAuthGateway(this WebApplication app, NodeOptions node)
    {
        foreach (var action in new[] { "login", "refresh", "logout", "desktop/approve", "desktop/redeem", "qr/start", "qr/cancel" })
            app.MapPost("/api/auth/" + action, (HttpContext context, IHttpClientFactory clients) =>
                Forward(context, clients, node.AuthUrl, action)).AllowAnonymous();
        app.MapGet("/api/auth/credential-policy", (HttpContext context, IHttpClientFactory clients) =>
            Forward(context, clients, node.AuthUrl, "credential-policy")).AllowAnonymous();
        app.MapGet("/api/auth/presentation", (HttpContext context, IHttpClientFactory clients) =>
            Forward(context, clients, node.AuthUrl, "presentation")).RequireAuthorization();
        app.MapPost("/api/auth/presentation", (HttpContext context, IHttpClientFactory clients) =>
            Forward(context, clients, node.AuthUrl, "presentation")).RequireAuthorization();
        foreach (var action in new[] { "sessions", "sessions/current", "consents", "device-actions" })
            app.MapGet("/api/auth/" + action, (HttpContext context, IHttpClientFactory clients) =>
                Forward(context, clients, node.AuthUrl, action)).RequireAuthorization();
        foreach (var action in new[] { "sessions/revoke-others", "consents", "device-grants/revoke", "device-grants/approve", "qr/inspect", "qr/approve", "device-actions/mcp", "device-actions/decide" })
            app.MapPost("/api/auth/" + action, (HttpContext context, IHttpClientFactory clients) =>
                Forward(context, clients, node.AuthUrl, action)).RequireAuthorization();
        app.MapPost("/api/auth/sessions/{id:guid}/revoke", (Guid id, HttpContext context, IHttpClientFactory clients) =>
            Forward(context, clients, node.AuthUrl, "sessions/" + id.ToString("D") + "/revoke")).RequireAuthorization();
        if (node.Kind is NodeKind.AuditOrchestra or NodeKind.Orchestra)
        {
            app.MapPost("/api/auth/audit/register", (HttpContext context, IHttpClientFactory clients) =>
                Forward(context, clients, node.AuthUrl, "audit/register")).AllowAnonymous();
            app.MapGet("/api/auth/audit/registration-documents", (HttpContext context, IHttpClientFactory clients) =>
                Forward(context, clients, node.AuthUrl, "audit/registration-documents")).AllowAnonymous();
        }
    }

    private static async Task Forward(HttpContext context, IHttpClientFactory factory, string auth, string action)
    {
        if (context.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            limit.MaxRequestBodySize = 16_384;
        using var client = factory.CreateClient("mysticego-logged-api");
        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), auth.TrimEnd('/') + "/api/auth/" + action);
        if ((action is "login" or "refresh" or "desktop/redeem" or "qr/start") && context.Request.Headers.UserAgent.ToString() is { Length: >0 } agent)
            request.Headers.TryAddWithoutValidation("User-Agent", agent.Length > 500 ? agent[..500] : agent);
        if (context.Connection.RemoteIpAddress is { } caller)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", caller.ToString());
        if ((action == "presentation" || action.StartsWith("sessions",StringComparison.Ordinal) || action == "consents" || action.StartsWith("device-grants/",StringComparison.Ordinal) || action.StartsWith("device-actions",StringComparison.Ordinal) || action is "qr/inspect" or "qr/approve") && AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization.ToString(),out var authorization) && authorization.Scheme == "Bearer")
            request.Headers.Authorization = authorization;
        if (HttpMethods.IsPost(context.Request.Method))
        {
            request.Content = new StreamContent(context.Request.Body);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }
        try
        {
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            timer.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await client.SendAsync(request, timer.Token);
            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.ContentType = response.Content.Headers.ContentType?.ToString();
            context.Response.Headers.CacheControl = "no-store";
            await response.Content.CopyToAsync(context.Response.Body, timer.Token);
        }
        catch (Exception error) when (error is HttpRequestException || error is OperationCanceledException && !context.RequestAborted.IsCancellationRequested)
        {
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsJsonAsync(new { error = "Центральный Auth API недоступен." });
            }
        }
    }
}
