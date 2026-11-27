using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.IdentityModel.Tokens;
namespace MysticEgo.Distributed;
public static class NodeSecurity
{
    public static void Validate(NodeOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.NodeId)) throw new InvalidOperationException("Не задан обязательный параметр Node:NodeId");
        if (o.DeploymentMode is not ("single-machine" or "shared-control-plane" or "remote-distributed"))
            throw new InvalidOperationException("Неизвестный режим развёртывания Node:DeploymentMode");
    }
    public static void AddSecurity(WebApplicationBuilder builder,NodeOptions options)
    {
        var key=builder.Configuration["Authorization:Jwt:SecretKey"]??"";
        var publicKeyPath=builder.Configuration["Authorization:Jwt:PublicKeyPath"];
        if (string.IsNullOrWhiteSpace(publicKeyPath) && key.Length<32) throw new InvalidOperationException("Необходимо задать публичный ключ JWT или надёжный секрет");
        var issuer=builder.Configuration["Authorization:Jwt:Issuer"]??"MysticEgo";
        var audience=builder.Configuration["Authorization:Jwt:Audience"]??"MysticEgo.Clients";
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(jwt=>
        {
            jwt.MapInboundClaims=true;
            jwt.Events=new JwtBearerEvents { OnTokenValidated=async context=>
            {
                if(context.Principal?.FindFirst("mystic_session") is null)return;
                var auth=context.Request.Headers.Authorization.ToString();
                // Only the configured Auth origin receives the bearer. No redirect or proxy.
                if(!System.Net.Http.Headers.AuthenticationHeaderValue.TryParse(auth,out var header)||header.Scheme!="Bearer")
                {context.Fail("Missing account bearer");return;}
                try
                {
                    var factory=context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>();
                    using var client=factory.CreateClient("mysticego-logged-api");
                    using var request=new HttpRequestMessage(HttpMethod.Get,options.AuthUrl.TrimEnd('/')+"/api/auth/sessions/current");
                    request.Headers.Authorization=header;
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(context.HttpContext.RequestAborted);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    using var response=await client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
                    if(!response.IsSuccessStatusCode)context.Fail("Account session revoked or unavailable");
                }
                catch(Exception error) when(error is HttpRequestException or OperationCanceledException or InvalidOperationException)
                {context.Fail("Central session validation unavailable");}
            }};
            jwt.TokenValidationParameters=new TokenValidationParameters
            {
                ValidateIssuer=true,ValidIssuer=issuer,ValidateAudience=true,ValidAudience=audience,
                ValidateIssuerSigningKey=true,IssuerSigningKey=Models.Autorization.Security.JwtSigningKeys.VerificationKey(new Models.Autorization.Configuration.JwtOptions{PublicKeyPath=publicKeyPath,SecretKey=key}),
                ValidateLifetime=true,ClockSkew=TimeSpan.FromSeconds(15),
                NameClaimType=ClaimTypes.Name,RoleClaimType=ClaimTypes.Role
            };

        });
        builder.Services.AddAuthorization(o=>
        {
            o.AddPolicy("device-agent", policy => policy.RequireAuthenticatedUser().RequireClaim("token_use","device").RequireRole("agent","audit")
                .RequireAssertion(async context =>
                {
                    if (!options.Managed || options.Kind is not (NodeKind.Global or NodeKind.Local)) return true;
                    return context.Resource is HttpContext request && context.User.IsInRole("agent") &&
                        Guid.TryParse(context.User.FindFirst("identity_id")?.Value, out var agent) &&
                        await request.RequestServices.GetRequiredService<PeerAdmission>().Allow(agent, request.RequestAborted);
                }));
            o.AddPolicy("operator",p=>p.RequireAuthenticatedUser().RequireAssertion(ctx=>ctx.User.FindFirst("token_use")?.Value!="device"));
            o.AddPolicy("node-peer",p=>p.RequireAuthenticatedUser().RequireClaim("token_use","device").RequireRole("local","global","agent","audit"));
        });
    }
}
public sealed class ClusterAgentHub : Models.Commands.SignalR.Hub.AgentHubBase
{
    public ClusterAgentHub(Models.Commands.SignalR.Hub.IClientRegistry r,Models.Commands.SignalR.Hub.ICapabilityStore c,Models.Commands.SignalR.Hub.IExecutionDispatcher d,Models.Commands.SignalR.Hub.IClientIdProvider p,Models.Commands.SignalR.Hub.IHubErrorHandler e,Microsoft.Extensions.Logging.ILogger<ClusterAgentHub> log):base(r,c,d,p,e,log){}
}
