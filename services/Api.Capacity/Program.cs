using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Models.Autorization.Security;
using Models.Autorization.Configuration;
using MysticEgo.Capacity;
using MysticEgo.Api.Capacity;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"]??"http://127.0.0.1:5480");
var config=builder.Configuration;
var keyPath=config["Authorization:Jwt:PublicKeyPath"]??throw new InvalidOperationException("Public JWT key required");
var issuer=config["Authorization:Jwt:Issuer"]??"MysticEgo";
var audience=config["Authorization:Jwt:Audience"]??"MysticEgo.Clients";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o=>
{
    o.TokenValidationParameters=new TokenValidationParameters
    {
        ValidateIssuer=true,ValidIssuer=issuer,ValidateAudience=true,ValidAudience=audience,
        ValidateLifetime=true,ValidateIssuerSigningKey=true,ValidAlgorithms=[SecurityAlgorithms.RsaSha256],
        IssuerSigningKey=JwtSigningKeys.VerificationKey(new JwtOptions{PublicKeyPath=keyPath}),ClockSkew=TimeSpan.FromSeconds(15)
    };
});
var owner=config["Capacity:OwnerSubject"]??throw new InvalidOperationException("Capacity:OwnerSubject required");
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("intake",p=>p.RequireAuthenticatedUser().RequireClaim("token_use","device"))
    .AddPolicy("reader",p=>p.RequireAuthenticatedUser().RequireAssertion(c=>!c.User.HasClaim("token_use","device")&&
        c.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value==owner));
builder.Services.AddTrafficProtection(config);
builder.Services.AddSingleton<IndependentCapacityMonitor>();
builder.Services.AddHostedService(sp=>sp.GetRequiredService<IndependentCapacityMonitor>());
builder.Services.AddHttpClient("local-evaluation",c=>{c.Timeout=TimeSpan.FromSeconds(20);c.MaxResponseContentBufferSize=65536;})
    .ConfigurePrimaryHttpMessageHandler(()=>new HttpClientHandler{AllowAutoRedirect=false,UseProxy=false,UseCookies=false});
var app=builder.Build();
app.UseMysticWebSecurity();
app.UseRouting();app.UseRateLimiter();app.UseAuthentication();app.UseAuthorization();
app.MapPost("/api/v1/stations/capacity",(CapacitySample sample,HttpContext context,IndependentCapacityMonitor monitor)=>
{
    if(!Guid.TryParse(context.User.FindFirst("identity_id")?.Value,out var identity))return Results.Forbid();
    return monitor.Accept(identity,sample)?Results.Accepted():Results.BadRequest(new{code="sample_rejected"});
}).RequireAuthorization("intake");
app.MapGet("/api/v1/capacity",(IndependentCapacityMonitor monitor)=>monitor.Read() is { } view?Results.Ok(view):Results.StatusCode(503))
    .RequireAuthorization("reader");
app.MapGet("/health",()=>Results.Ok(new{status="ok"}));
app.Run();
