using MysticEgo.Avatar;
using MysticEgo.Distributed;
var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o=>o.ServiceName="MysticEgo.Global");
var node=Bootstrap.ConfigureNode(builder,NodeKind.Global);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<NodeAnnouncer>();
builder.Services.Configure<AgentUpdateOptions>(builder.Configuration.GetSection("AgentUpdates"));
builder.Services.AddSingleton(sp=>sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentUpdateOptions>>().Value);
builder.Services.AddSingleton<LangameSessionCache>();
builder.Services.AddSingleton<IAgentReleaseService,AgentReleaseService>();
builder.Services.AddSingleton<ILangameService,LangameService>();
builder.Services.AddAvatar(AvatarNode.Global);
builder.Services.AddSocial();
builder.Services.AddControllers();
builder.Services.AddVpnServices();
var app=builder.Build();
Models.Autorization.Security.WebSecurityHeaders.UseMysticWebSecurity(app);
app.UsePanelSocialPreviews();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseNode();
app.MapControllers();







if(File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();
