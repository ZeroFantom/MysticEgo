using MysticEgo.Avatar;
using MysticEgo.Distributed;
var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o=>o.ServiceName="MysticEgo.Local");
var node=Bootstrap.ConfigureNode(builder,NodeKind.Local);
builder.Services.AddHttpClient();
builder.Services.AddHostedService<NodeAnnouncer>();
builder.Services.AddSingleton<LangameSessionCache>();
builder.Services.AddSingleton<ILangameService,LangameService>();
builder.Services.AddAvatar(AvatarNode.Local);
builder.Services.AddControllers();
builder.Services.Configure<AgentUpdateOptions>(builder.Configuration.GetSection("AgentUpdates"));
builder.Services.AddSingleton(sp=>sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentUpdateOptions>>().Value);
builder.Services.AddSingleton<IAgentReleaseService, AgentReleaseService>();
var app=builder.Build();
app.UsePanelSocialPreviews();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseNode();
app.MapControllers();




if(File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();
