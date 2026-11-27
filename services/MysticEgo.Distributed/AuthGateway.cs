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
        foreach (var action in new[] { "login", "refresh", "logout" })
            app.MapPost("/api/auth/" + action, (HttpContext context, IHttpClientFactory clients) =>
                Forward(context, clients, node.AuthUrl, action)).AllowAnonymous();
        app.MapGet("/api/auth/credential-policy", (HttpContext context, IHttpClientFactory clients) =>
            Forward(context, clients, node.AuthUrl, "credential-policy")).AllowAnonymous();
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
        if (context.Connection.RemoteIpAddress is { } caller)
            request.Headers.TryAddWithoutValidation("X-Forwarded-For", caller.ToString());
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
