using Microsoft.Extensions.DependencyInjection.Extensions;
using Models.Autorization.Security;
using Models.Commands.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Models.Commands.SignalR.Hub;
namespace MysticEgo.Distributed;
public static class Bootstrap
{
    public static NodeOptions ConfigureNode(WebApplicationBuilder builder,NodeKind kind)
    {
        var opt=new NodeOptions();builder.Configuration.GetSection("Node").Bind(opt);opt.Kind=kind;
        NodeSecurity.Validate(opt);
        builder.Services.AddSingleton(opt);
        builder.Services.AddSingleton<NodeState>();
        builder.Services.AddNodeApiServices();
        builder.Services.TryAddSingleton<Models.Commands.Scanner.ICapabilityScanner, Models.Commands.Scanner.CapabilityScanner>();
        builder.Services.AddSingleton<CommandCatalog>();
        builder.Services.AddSingleton<ApiMethodCatalog>();
        builder.Services.AddCommandMcpServer();
        builder.Services.AddUiAutomationBridge();
        builder.Services.AddSingleton<AvatarHandoffQueue>();
        builder.Services.AddLocalAssistant();
        builder.Services.AddScoped<DiagnosticMcpService>();
        builder.Services.AddSingleton<InterfaceAutomation>();
        builder.Services.AddEther();
        builder.Services.AddSetup();
        builder.Services.AddControllers().AddApplicationPart(typeof(NodeController).Assembly);
        builder.Services.AddHttpClient();
        builder.Services.AddHttpClient("mysticego-logged-api", client => client.Timeout = TimeSpan.FromSeconds(140))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false });
        builder.Services.AddOptionalServices();
        builder.Services.AddSingleton<IdentityTokenSource>();
        builder.Services.AddSingleton<PeerAdmission>();
        builder.Services.AddSignalR().AddJsonProtocol(p=>p.PayloadSerializerOptions=Models.Commands.SignalR.ContractsJsonOptions.Create());
        
        builder.Services.AddCommandServerCore().AddExecutionDispatcherFor<ClusterAgentHub>();
        builder.Services.AddTrafficProtection(builder.Configuration);
        NodeSecurity.AddSecurity(builder,opt);
        builder.Services.AddCors(cors=>cors.AddPolicy("ui",p=>
        {
            var origins=builder.Configuration.GetSection("AllowedOrigins").Get<string[]>()??Array.Empty<string>();
            if(origins.Length>0)p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod();
        }));
        return opt;
    }
    public static void UseNode(this WebApplication app,bool hub=true)
    {
        var vision=app.Configuration["Vision:AssetDirectory"];
        if(!string.IsNullOrWhiteSpace(vision) && Directory.Exists(vision))
        {
            var types=new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            types.Mappings[".task"]="application/octet-stream";
            app.UseStaticFiles(new StaticFileOptions{RequestPath="/vision",FileProvider=new Microsoft.Extensions.FileProviders.PhysicalFileProvider(vision),ContentTypeProvider=types});
        }
        if (app.Configuration["Avatar:BundledDirectory"] is { } bundle && Directory.Exists(bundle))
        {
            var types = new Microsoft.AspNetCore.StaticFiles.FileExtensionContentTypeProvider();
            types.Mappings[".vrm"] = "model/gltf-binary";
            app.UseStaticFiles(new StaticFileOptions { RequestPath = "/bundled", FileProvider =
                new Microsoft.Extensions.FileProviders.PhysicalFileProvider(bundle), ContentTypeProvider = types });
        }
        app.UseRouting();
        app.UseCors("ui");app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
        var node = app.Services.GetRequiredService<NodeOptions>();
        // Validate the selected database before reporting service readiness.
        _ = app.Services.GetRequiredService<NodeState>();
        app.UseCentralConnection(node);
        app.MapAuthGateway(node);
        app.MapGet("/api/v1/public/runtime", () => new { authUrl = node.BrowserAuthUrl ?? node.AuthUrl, nodeId = node.NodeId })
            .AllowAnonymous();
        if(hub)app.MapHub<ClusterAgentHub>("/hub/agents").RequireAuthorization("device-agent");
    }
}
