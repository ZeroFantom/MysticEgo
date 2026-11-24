using System.Text.Json;
using System.Text.Json.Serialization;
using Models.Autorization.Contracts.Responses;
using Models.Autorization.Internal;

namespace Api.Auth.Middleware
{
    /// <summary>
    /// Ловит AuthException (ValidationException, AuthAuthException, ForbiddenException)
    /// и превращает в ApiErrorDto с корректным HTTP-статусом.
    /// Всё остальное — 500 + лог.
    /// </summary>
    public sealed class ExceptionHandlingMiddleware
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionHandlingMiddleware> _log;
        private readonly IHostEnvironment _env;

        public ExceptionHandlingMiddleware(
            RequestDelegate next,
            ILogger<ExceptionHandlingMiddleware> log,
            IHostEnvironment env)
        {
            _next = next;
            _log = log;
            _env = env;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            try { await _next(ctx); }
            catch (AuthException ex)
            {
                await WriteAsync(ctx, ex.Status, ex.Code, ex.Message);
            }
            catch (OperationCanceledException) when (ctx.RequestAborted.IsCancellationRequested)
            {
                _log.LogDebug("Request aborted by client.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Unhandled: {Method} {Path}",
                    ctx.Request.Method, ctx.Request.Path);

                if (ctx.Response.HasStarted) throw;

                await WriteAsync(ctx, 500, "internal_error",
                    _env.IsDevelopment() ? ex.Message : "Внутренняя ошибка сервера.");
            }
        }

        private static async Task WriteAsync(
            HttpContext ctx, int status, string code, string message)
        {
            ctx.Response.Clear();
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";

            await ctx.Response.WriteAsync(JsonSerializer.Serialize(new ApiErrorDto
            {
                Code = code,
                Message = message,
                TraceId = ctx.TraceIdentifier,
            }, Json));
        }
    }
}