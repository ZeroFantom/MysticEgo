using Microsoft.AspNetCore.Authorization;
using MysticEgo.Api.Orchestra;
using MysticEgo.Avatar;
using MysticEgo.WorldAI;
using MysticEgo.Soul;
using MysticEgo.Distributed;
var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o=>o.ServiceName="MysticEgo.Orchestra");
var options=Bootstrap.ConfigureNode(builder,NodeKind.Orchestra);
builder.Services.AddWorldAi();
builder.Services.AddWorldAiQueue();
builder.Services.AddControllers().AddApplicationPart(typeof(WorldAiController).Assembly);
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("mysticego-timeweb").ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
builder.Services.AddSingleton<StationRegistry>();
builder.Services.AddSingleton<NativeDeploymentJobs>();
builder.Services.AddSingleton<INativeDeploymentPlanService,NativeDeploymentPlanService>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<NativeDeploymentJobs>());
builder.Services.AddSoul();
builder.Services.AddAvatar(AvatarNode.Orchestra);
builder.Services.AddSocial();
builder.Services.AddSingleton<MysticEgo.Api.Orchestra.Controllers.StickerStore>();
builder.Services.AddSingleton<ManagedNetwork>();
builder.Services.AddSingleton<DevelopmentTeam>();
builder.Services.AddSingleton<ParentAuthorization>();
builder.Services.AddAuthorizationBuilder().AddPolicy("orchestra-executor", policy => policy
    .RequireAuthenticatedUser().RequireClaim("token_use", "device").RequireRole("agent")
    .RequireAssertion(context => context.Resource is HttpContext request &&
        Guid.TryParse(context.User.FindFirst("identity_id")?.Value, out var id) &&
        request.RequestServices.GetRequiredService<ManagedNetwork>().Find(id) is { Kind: "agent", Enabled: true }));
builder.Services.AddSingleton<ManagedDeployment>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ManagedDeployment>());
builder.Services.AddSingleton<ServerTelemetry>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ServerTelemetry>());
builder.Services.AddOrchestraProxy();
builder.Services.AddSingleton<GitHubPanel>();
builder.Services.AddHttpClient("panel-github",client=>{
    client.Timeout=TimeSpan.FromSeconds(8);
    client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    client.DefaultRequestHeaders.UserAgent.ParseAdd("MysticEgo-Orchestra");
}).ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false});
builder.Services.AddHttpClient("panel-model",client=>client.Timeout=TimeSpan.FromSeconds(55))
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false});
var app=builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseNode(hub:false);
app.MapHub<ClusterAgentHub>("/hub/agents").RequireAuthorization("orchestra-executor");
// Высший уровень — строго для команды разработки. К обычным API другие роли не допускаются.
app.Use(async (context,next)=>
{
    var team = context.RequestServices.GetRequiredService<DevelopmentTeam>();
    var path = context.Request.Path;
    if (path.StartsWithSegments("/api/v1/managed") && !team.IsOwner(context.User) &&
        await team.IsMember(context.User, context.RequestAborted))
        context.Items[typeof(DevelopmentTeam)] = true;
    var teamRead = HttpMethods.IsGet(context.Request.Method) &&
        (path.Equals("/api/v1/overview", StringComparison.OrdinalIgnoreCase) ||
         path.Equals("/api/v1/deployment", StringComparison.OrdinalIgnoreCase) ||
         path.Equals("/api/v1/meta", StringComparison.OrdinalIgnoreCase)) &&
        await team.IsMember(context.User, context.RequestAborted);
    if (teamRead) context.User.AddIdentity(new System.Security.Claims.ClaimsIdentity(
        [new System.Security.Claims.Claim("permission", "node.read")]));
    if(path.StartsWithSegments("/api/v1") &&
       !context.Request.Path.StartsWithSegments("/api/v1/public") &&
       !context.Request.Path.StartsWithSegments("/api/v1/collective") &&
       !context.Request.Path.StartsWithSegments("/api/v1/faq") &&
       !context.Request.Path.StartsWithSegments("/api/v1/avatars/account") &&
       !context.Request.Path.StartsWithSegments("/api/v1/assistant/workbench") &&
       !context.Request.Path.StartsWithSegments("/api/v1/assistant/persona") &&
       !context.Request.Path.Equals("/api/v1/assistant/polish", StringComparison.OrdinalIgnoreCase) &&
       !context.Request.Path.Equals("/api/v1/ecosystem/capabilities", StringComparison.OrdinalIgnoreCase) &&
       !context.Request.Path.StartsWithSegments("/api/v1/managed") &&
       !context.Request.Path.Equals("/api/v1/stations/check-in",StringComparison.OrdinalIgnoreCase) &&
       !context.Request.Path.Equals("/api/v1/setup/enroll",StringComparison.OrdinalIgnoreCase) &&
       !context.Request.Path.Equals("/api/v1/nodes/announce",StringComparison.OrdinalIgnoreCase) &&
       !team.IsOwner(context.User) && !teamRead)
    {context.Response.StatusCode=403;return;}
    await next(context);
});





app.MapGet("/metrics", (HttpContext request, ServerTelemetry telemetry) =>
    request.Connection.RemoteIpAddress is { } address && System.Net.IPAddress.IsLoopback(address)
        ? Results.Text(telemetry.Prometheus(), "text/plain; version=0.0.4")
        : Results.NotFound());
app.MapGet("/audit-portal/release-info.json", (IConfiguration configuration) =>
{
    var webRoot = configuration["Managed:AuditWebRoot"];
    var file = webRoot is null ? null : Path.Combine(webRoot, "release-info.json");
    return file is not null && File.Exists(file) ? Results.File(file, "application/json") : Results.NotFound();
}).AllowAnonymous();
app.MapGet("/audit-portal/{**route}", (IConfiguration configuration) =>
{
    var webRoot = configuration["Managed:AuditWebRoot"];
    var file = webRoot is null ? null : Path.Combine(webRoot, "index.html");
    return file is not null && File.Exists(file) ? Results.File(file, "text/html; charset=utf-8") : Results.NotFound();
}).AllowAnonymous();
app.MapControllers();
// Доступ к центру разработки ограничен правами system.developer / system.bypass.
if(File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();
