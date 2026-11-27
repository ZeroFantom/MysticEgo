using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MysticEgo.Distributed;

/// <summary>Optional HTTPS same-origin browser session boundary. Refresh credentials are opaque
/// server-validated tokens in a host-only cookie; ordinary API/MCP authentication remains Bearer.</summary>
public static class BrowserSessionGateway
{
    public const string CookieName="__Host-MysticEgoRefresh";
    public static bool OriginAllowed(string? expected,string? origin,string? host)
    {
        return Uri.TryCreate(expected,UriKind.Absolute,out var u)&&u.Scheme=="https"&&u.UserInfo.Length==0&&
            u.AbsolutePath=="/"&&u.Query.Length==0&&u.Fragment.Length==0&&
            string.Equals(u.GetLeftPart(UriPartial.Authority),origin,StringComparison.Ordinal)&&
            string.Equals(u.Authority,host,StringComparison.OrdinalIgnoreCase);
    }
    public static CookieOptions Options()=>new(){Secure=true,HttpOnly=true,SameSite=SameSiteMode.Strict,Path="/",IsEssential=true};

    public static void MapBrowserSessions(this WebApplication app,NodeOptions node)
    {
        var origin=app.Configuration["BrowserSession:Origin"];
        if(!string.IsNullOrWhiteSpace(origin)&&(!Uri.TryCreate(origin,UriKind.Absolute,out var parsed)||!OriginAllowed(origin,parsed.GetLeftPart(UriPartial.Authority),parsed.Authority)))
            throw new InvalidOperationException("BrowserSession:Origin must be an HTTPS origin without path, query or credentials.");
        app.MapGet("/api/auth/browser/status",()=>Results.Ok(new{enabled=!string.IsNullOrWhiteSpace(origin)})).AllowAnonymous();
        foreach(var action in new[]{"login","refresh","logout"})
            app.MapPost("/api/auth/browser/"+action,(HttpContext context,IHttpClientFactory clients)=>
                Handle(context,clients,node.AuthUrl,origin,action)).AllowAnonymous();
    }
    private static async Task Handle(HttpContext context,IHttpClientFactory clients,string auth,string? origin,string action)
    {
        context.Response.Headers.CacheControl="no-store";
        if(!OriginAllowed(origin,context.Request.Headers.Origin.ToString(),context.Request.Host.Value)||
            !string.Equals(context.Request.Headers["X-Mystic-Browser"].ToString(),"1",StringComparison.Ordinal)||
            !context.Request.HasJsonContentType())
        {context.Response.StatusCode=403;return;}
        if(context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is {IsReadOnly:false} size)
            size.MaxRequestBodySize=16384;
        // Reject ambiguous duplicate refresh cookie names instead of picking one by ordering.
        if(context.Request.Headers.Cookie.ToString().Split(';',StringSplitOptions.TrimEntries).Count(c=>c.StartsWith(CookieName+"=",StringComparison.Ordinal))>1)
        {context.Response.StatusCode=400;return;}
        var cookie=context.Request.Cookies[CookieName];
        if(action!="login"&&(string.IsNullOrEmpty(cookie)||cookie.Length>500))
        {
            context.Response.StatusCode=action=="logout"?204:401;return;
        }
        using var client=clients.CreateClient("mysticego-logged-api");
        using var request=new HttpRequestMessage(HttpMethod.Post,auth.TrimEnd('/')+"/api/auth/"+action);
        if(context.Connection.RemoteIpAddress is { } ip)request.Headers.TryAddWithoutValidation("X-Forwarded-For",ip.ToString());
        var ua=context.Request.Headers.UserAgent.ToString();
        if(ua.Length>0)request.Headers.TryAddWithoutValidation("User-Agent",ua[..Math.Min(ua.Length,500)]);
        request.Content=action=="login"?new StreamContent(context.Request.Body):JsonContent.Create(new{refreshToken=cookie});
        if(action=="login")request.Content.Headers.ContentType=new("application/json");
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            using var result=await client.SendAsync(request,deadline.Token);
            if(!result.IsSuccessStatusCode)
            {
                if(action!="login"&&result.StatusCode==System.Net.HttpStatusCode.Unauthorized)context.Response.Cookies.Delete(CookieName,Options());
                context.Response.StatusCode=(int)result.StatusCode;return;
            }
            if(action=="logout")
            {context.Response.Cookies.Delete(CookieName,Options());context.Response.StatusCode=204;return;}
            var data=JsonNode.Parse(await result.Content.ReadAsStringAsync(deadline.Token)) as JsonObject;
            if(data?["refreshToken"] is not JsonValue value||!value.TryGetValue<string>(out var refresh)||string.IsNullOrEmpty(refresh)||refresh.Length>500||
                data["accessToken"] is not JsonValue access||!access.TryGetValue<string>(out var jwt)||string.IsNullOrEmpty(jwt))
            {context.Response.StatusCode=502;return;}
            data.Remove("refreshToken");
            context.Response.Cookies.Append(CookieName,refresh,Options());
            await context.Response.WriteAsJsonAsync(data,deadline.Token);
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException || ex is OperationCanceledException&&!context.RequestAborted.IsCancellationRequested)
        {if(!context.Response.HasStarted)context.Response.StatusCode=502;}
    }
}
