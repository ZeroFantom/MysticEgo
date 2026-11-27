using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using System.Text.Json.Serialization;
using Api.Auth.Middleware;
using Models.Autorization.Extensions;
using Models.Autorization.Security;

namespace Api.Auth;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Host.UseWindowsService(o=>o.ServiceName="MysticEgo.Auth");

        var url = builder.Configuration["Urls:Http"]
                  ?? "http://127.0.0.1:5400";
        builder.WebHost.UseUrls(url);

        // ── Ядро авторизации
        builder.Services.AddAuthorizationFramework(
            builder.Configuration,
            sectionName: "Authorization");

        // ── MVC + глобальный ToolAuthorizationFilter + JSON
        builder.Services
            .AddControllers(o =>
            {
                // Глобальный фильтр — проверяет права на каждый запрос.
                // Отключается через [AllowAnonymous] / [SkipPermission] / [SkipTool].
                o.AddToolAuthorizationFilter();
            })
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.DefaultIgnoreCondition =
                    JsonIgnoreCondition.WhenWritingNull;
                o.JsonSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter());
            });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

        // Разрешённые адреса трёх фронтендов берём из конфигурации.
        // localhost и 127.0.0.1 — разные Origin для браузера.
        var allowedOrigins = builder.Configuration
            .GetSection("AllowedOrigins")
            .Get<string[]>() ?? Array.Empty<string>();

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.WithOrigins(allowedOrigins)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });

        builder.Services.AddHealthChecks();
        builder.Services.AddTrafficProtection(builder.Configuration);
        builder.Services.AddRateLimiter(options=>
        {
            options.RejectionStatusCode=429;
            options.AddPolicy("auth-sensitive",ctx=>RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions
                {PermitLimit=120,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}));
            options.AddPolicy("audit-signup",ctx=>RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions
                {PermitLimit=3,Window=TimeSpan.FromMinutes(15),QueueLimit=0,AutoReplenishment=true}));
        });

        var app = builder.Build();

        await app.UseAuthorizationAsync();

        // Only localhost reverse proxies may supply the original client address by default.
        app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor });
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseRouting();
        app.UseCors();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapAuditRegistration();
        // Public metadata only. Never accepts or returns a password.
        app.MapGet("/api/auth/credential-policy", (Models.Autorization.Configuration.AuthorizationOptions options) =>
            Results.Ok(Models.Autorization.Security.CredentialPolicy.Describe(options.PasswordPolicy))).AllowAnonymous();

        app.MapGet("/health", () => Results.Json(new
        {
            status = "ok",
            service = "MysticEgo.Authorization.Service",
            timestamp = DateTime.UtcNow,
        }));

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json",
                    "MysticEgo.Authorization v1");
                c.RoutePrefix = "swagger";
            });
        }

        await app.RunAsync();
    }
}