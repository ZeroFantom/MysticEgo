using MysticEgo.Soul;
using MysticEgo.Avatar;
using MysticEgo.Distributed;
using Models.Commands.Scanner;
using Models.Commands;
using Models.Commands.SignalR.Clients;
var builder=WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o=>o.ServiceName="MysticEgo.AuditOrchestra");
var options=Bootstrap.ConfigureNode(builder,NodeKind.AuditOrchestra);
builder.Services.AddSingleton<ICapabilityScanner,CapabilityScanner>();
builder.Services.AddCommandClientRuntime(o=>{o.ClientId=Guid.TryParse(options.IdentityId,out var id)?id:Guid.Empty;o.HubUrl="http://localhost/placeholder";o.CommandAssemblies=new[]{typeof(SystemInfoCommand).Assembly};});
// У домашнего компьютера удалённые команды выключены, пока владелец явно не включит их.
var allowRemote=builder.Configuration.GetValue<bool>("Audit:AllowRemoteCommands");
if(!allowRemote){options.GlobalUrl=null;options.LocalUrl=null;}
builder.Services.AddHostedService<AgentBackgroundService>();
builder.Services.AddHostedService<NodeAnnouncer>();
builder.Services.AddControllers();
builder.Services.AddNetworkRepair(builder.Configuration);
builder.Services.AddAvatar(AvatarNode.Audit);
builder.Services.AddSoul();
builder.Services.AddHomeAssessment();
var app=builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseNode(hub:false);
// Отдельное разрешение владельца требуется для операций над домашним ПК.
app.Use(async(context,next)=>
{
    if(context.Request.Path.StartsWithSegments("/api/v1/commands") &&
       !(context.User.Identity?.IsAuthenticated==true &&
         context.User.FindFirst("token_use")?.Value!="device" &&
         string.Equals(context.User.Identity.Name,
           Environment.GetEnvironmentVariable("MYSTICEGO_AGENT_OWNER_USER"),
           StringComparison.Ordinal)))
    {context.Response.StatusCode=403;return;}
    await next(context);
});
app.MapControllers();





if(File.Exists(Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html")))
    app.MapFallbackToFile("index.html");
app.Run();

