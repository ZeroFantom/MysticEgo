using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Models.Autorization.Security;

/// <summary>Common browser response hardening. Does not treat cookies as API authentication credentials.</summary>
public static class WebSecurityHeaders
{
    public static IApplicationBuilder UseMysticWebSecurity(this IApplicationBuilder app) =>
        app.Use(async (context,next)=>
        {
            context.Response.OnStarting(()=>
            {
                var headers=context.Response.Headers;
                headers.TryAdd("X-Content-Type-Options","nosniff");
                headers.TryAdd("Referrer-Policy","no-referrer");
                headers.TryAdd("X-Frame-Options","SAMEORIGIN");
                headers.TryAdd("Content-Security-Policy","frame-ancestors 'self'; object-src 'none'; base-uri 'self'");
                if(context.Request.Path.StartsWithSegments("/api/auth"))headers.CacheControl="no-store";
                return Task.CompletedTask;
            });
            await next(context);
        });
}
