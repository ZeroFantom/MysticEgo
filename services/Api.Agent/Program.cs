using MysticEgo.Avatar;
using MysticEgo.Distributed;
using Models.Commands.Scanner;
using Models.Commands.SignalR.Clients;
var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(options=>options.ServiceName="MysticEgo.Agent");
var node=Bootstrap.ConfigureNode(builder,NodeKind.Agent);
builder.Services.AddSingleton<ICapabilityScanner,CapabilityScanner>();
builder.Services.AddCommandClientRuntime(o=>{o.ClientId=Guid.TryParse(node.IdentityId,out var id)?id:Guid.Empty;o.HubUrl="http://localhost/placeholder";o.CommandAssemblies=new[]{typeof(SystemInfoCommand).Assembly};});
builder.Services.AddHostedService<AgentBackgroundService>();
builder.Services.AddHostedService<NodeAnnouncer>();
builder.Services.Configure<AgentUpdateOptions>(builder.Configuration.GetSection("AgentUpdates"));
builder.Services.AddSingleton(sp=>sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AgentUpdateOptions>>().Value);
builder.Services.AddSingleton<AgentUpdateState>();
builder.Services.AddSingleton<AgentUpdateWorker>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<AgentUpdateWorker>());
builder.Services.AddControllers();
builder.Services.AddNetworkRepair(builder.Configuration);
builder.Services.AddSingleton<InstallationJobs>();
builder.Services.AddSingleton<IVendorInstallationService,VendorInstallationService>();
builder.Services.AddSingleton<GuestSessions>();
builder.Services.AddAvatar(AvatarNode.Agent);
var app=builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseNode(hub:false);



app.MapControllers();



if(File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();
