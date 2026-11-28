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
        app.MapGet("/api/auth/browser/external/telegram/status",(HttpContext c,IHttpClientFactory clients)=>
            External(c,clients,node.AuthUrl,origin,"status")).AllowAnonymous();
        app.MapPost("/api/auth/browser/external/telegram/start",(HttpContext c,IHttpClientFactory clients)=>
            External(c,clients,node.AuthUrl,origin,"start")).AllowAnonymous();
        app.MapGet(ExternalCallback,(HttpContext c,IHttpClientFactory clients)=>
            External(c,clients,node.AuthUrl,origin,"callback")).AllowAnonymous();
        foreach(var action in new[]{"login","refresh","logout"})
            app.MapPost("/api/auth/browser/"+action,(HttpContext context,IHttpClientFactory clients)=>
                Handle(context,clients,node.AuthUrl,origin,action)).AllowAnonymous();
    }
    private const string ExternalCookie = "__Host-mystic-external-login";
    private const string ExternalCallback = "/api/auth/browser/external/telegram/callback";
    private static CookieOptions ExternalOptions()=>new(){Secure=true,HttpOnly=true,SameSite=SameSiteMode.Lax,Path="/",IsEssential=true,MaxAge=TimeSpan.FromMinutes(5)};
    private static bool ExternalBinding(string? value)=>value is {Length:43}&&value.All(c=>c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static async Task External(HttpContext context,IHttpClientFactory clients,string auth,string? origin,string action)
    {
        context.Response.Headers.CacheControl="no-store";
        context.Response.Headers["Referrer-Policy"]="no-referrer";
        var host=context.Request.Host.Value;
        if(!Uri.TryCreate(origin,UriKind.Absolute,out var u)||!OriginAllowed(origin,u.GetLeftPart(UriPartial.Authority),host))
        {context.Response.StatusCode=404;return;}
        var expected=u.GetLeftPart(UriPartial.Authority);
        bool callback=action=="callback";
        if(action=="start"&&(!OriginAllowed(origin,context.Request.Headers.Origin.ToString(),host)||
            context.Request.Headers["X-Mystic-Browser"].ToString()!="1"||!context.Request.HasJsonContentType()))
        {context.Response.StatusCode=403;return;}
        // Avoid silently replacing an already authenticated host-wide browser session.
        if(action!="status"&&context.Request.Cookies.ContainsKey(CookieName))
        {context.Response.StatusCode=409;return;}
        var browser=context.Request.Cookies[ExternalCookie];
        if(callback&&(context.Request.Query.ContainsKey("error")||context.Request.Query["state"].Count!=1||
            context.Request.Query["code"].Count!=1||!ExternalBinding(browser)||
            context.Request.Headers.Cookie.ToString().Split(';',StringSplitOptions.TrimEntries)
                .Count(c=>c.StartsWith(ExternalCookie+"=",StringComparison.Ordinal))!=1))
        {context.Response.StatusCode=400;return;}
        var state=context.Request.Query["state"].ToString();var code=context.Request.Query["code"].ToString();
        if(callback&&(!ExternalBinding(state)||code.Length is 0 or >4096||code.Any(char.IsControl)))
        {context.Response.StatusCode=400;return;}
        using var client=clients.CreateClient("mysticego-logged-api");
        using var request=new HttpRequestMessage(action=="status"?HttpMethod.Get:HttpMethod.Post,
            auth.TrimEnd('/')+"/api/auth/external/login/telegram/"+(callback?"complete":action));
        request.Headers.TryAddWithoutValidation("Origin",expected);
        request.Headers.TryAddWithoutValidation("X-Mystic-Browser","1");
        if(context.Connection.RemoteIpAddress is { } ip)request.Headers.TryAddWithoutValidation("X-Forwarded-For",ip.ToString());
        if(callback)
        {
            request.Headers.TryAddWithoutValidation("Cookie",ExternalCookie+"="+browser);
            request.Content=JsonContent.Create(new{state,code});
        }
        else if(action=="start")request.Content=JsonContent.Create(new{});
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        try
        {
            using var response=await client.SendAsync(request,deadline.Token);
            if(callback)context.Response.Cookies.Delete(ExternalCookie,ExternalOptions());
            if(!response.IsSuccessStatusCode){context.Response.StatusCode=(int)response.StatusCode;return;}
            var data=JsonNode.Parse(await response.Content.ReadAsStringAsync(deadline.Token)) as JsonObject;
            if(action=="status")
            {await context.Response.WriteAsJsonAsync(new{enabled=data?["enabled"]?.GetValue<bool>()==true},deadline.Token);return;}
            if(action=="start")
            {
                var url=data?["authorizationUrl"]?.GetValue<string>();
                if(!Uri.TryCreate(url,UriKind.Absolute,out var target)||target.Scheme!="https"||
                    target.Host!="oauth.telegram.org"||target.AbsolutePath!="/auth"||target.UserInfo.Length!=0||!target.IsDefaultPort||
                    !response.Headers.TryGetValues("Set-Cookie",out var values))
                {context.Response.StatusCode=502;return;}
                var bindings=values.Where(v=>v.StartsWith(ExternalCookie+"=",StringComparison.Ordinal))
                    .Select(v=>v.Split(';')[0][(ExternalCookie.Length+1)..]).ToArray();
                if(bindings.Length!=1||!ExternalBinding(bindings[0])){context.Response.StatusCode=502;return;}
                context.Response.Cookies.Append(ExternalCookie,bindings[0],ExternalOptions());
                await context.Response.WriteAsJsonAsync(new{authorizationUrl=target.AbsoluteUri},deadline.Token);return;
            }
            if(data?["refreshToken"] is not JsonValue value||!value.TryGetValue<string>(out var refresh)||
                string.IsNullOrEmpty(refresh)||refresh.Length>500||
                data["accessToken"] is not JsonValue access||!access.TryGetValue<string>(out var jwt)||string.IsNullOrEmpty(jwt))
            {context.Response.StatusCode=502;return;}
            context.Response.Cookies.Append(CookieName,refresh,Options());
            // Fixed, credential-free redirect. Frontend restoration is a separate explicit step.
            context.Response.Redirect("/?externalLogin=1");
        }
        catch(Exception ex) when(ex is HttpRequestException or JsonException or InvalidOperationException ||
            ex is OperationCanceledException&&!context.RequestAborted.IsCancellationRequested)
        {if(!context.Response.HasStarted)context.Response.StatusCode=502;}
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
