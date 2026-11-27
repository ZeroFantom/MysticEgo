using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Models.Autorization.Security;

static void Check(bool condition,string message)
{
    if(!condition)throw new InvalidOperationException(message);
}

// Security boundaries: proxy headers do not affect direct callers or authentication identity.
var context=new DefaultHttpContext();
context.Connection.RemoteIpAddress=IPAddress.Parse("192.0.2.10");
context.Request.Headers["X-Forwarded-For"]="198.51.100.2";
Check(TrafficProtection.ClientKey(context,new HashSet<IPAddress>())=="192.0.2.10","Untrusted forwarded IP accepted");
var trusted=new HashSet<IPAddress>{IPAddress.Parse("192.0.2.10")};
Check(TrafficProtection.ClientKey(context,trusted)=="198.51.100.2","Trusted single IP not accepted");
context.Request.Headers["X-Forwarded-For"]="198.51.100.2, 203.0.113.2";
Check(TrafficProtection.ClientKey(context,trusted)=="192.0.2.10","Ambiguous forwarded chain accepted");
Check(context.Connection.RemoteIpAddress.ToString()=="192.0.2.10","Admission mutated connection identity");
context.Connection.RemoteIpAddress=IPAddress.Parse("2001:db8:1:2::123");
Check(TrafficProtection.ClientKey(context,trusted)=="2001:db8:1:2::/64","IPv6 subnet normalization failed");
Check(TrafficProtection.Category("/mcp")=="expensive","MCP category");
Check(TrafficProtection.Category("/api/auth/login")=="sensitive","Login category");
Check(TrafficProtection.Category("/hub/agents")=="hub","Hub category");

// Local ephemeral Kestrel only. No traffic is sent to production.
var builder=WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>
{
    ["TrafficProtection:GlobalPerSecond"]="1000",
    ["TrafficProtection:ClientPerSecond"]="1000",
    ["TrafficProtection:SensitivePerMinute"]="2",
    ["TrafficProtection:ConcurrentExpensiveRequests"]="1",
    ["TrafficProtection:ExpensivePerSecond"]="100",
    ["TrafficProtection:ExpensiveClientPerSecond"]="100"
});
builder.Services.AddTrafficProtection(builder.Configuration);
await using var app=builder.Build();
app.UseRouting();
app.UseRateLimiter();
var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
app.MapPost("/mcp",async (CancellationToken ct)=>
{
    entered.TrySetResult();
    await release.Task.WaitAsync(ct);
    return Results.Ok();
});
app.MapPost("/api/auth/login",()=>Results.Ok());
app.MapGet("/health",()=>Results.Ok());
await app.StartAsync();
try
{
    var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client=new HttpClient{BaseAddress=new Uri(address),Timeout=TimeSpan.FromSeconds(10)};
    var first=client.PostAsync("/mcp",null);
    await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
    using var blocked=await client.PostAsync("/mcp",null);
    Check(blocked.StatusCode==HttpStatusCode.TooManyRequests,"Expensive concurrency limit not enforced");
    Check(blocked.Headers.RetryAfter!=null,"Retry-After missing");
    Check((await blocked.Content.ReadAsStringAsync()).Contains("rate_limited"),"Stable error code missing");
    using var health=await client.GetAsync("/health");
    Check(health.IsSuccessStatusCode,"Expensive request blocked ordinary capacity");
    release.TrySetResult();
    using var completed=await first;
    Check(completed.IsSuccessStatusCode,"Admitted request failed");
    using var login1=await client.PostAsync("/api/auth/login",null);
    using var login2=await client.PostAsync("/api/auth/login",null);
    using var login3=await client.PostAsync("/api/auth/login",null);
    Check(login1.IsSuccessStatusCode&&login2.IsSuccessStatusCode&&login3.StatusCode==HttpStatusCode.TooManyRequests,"Sensitive rate limit not enforced");
    Console.WriteLine("Traffic protection smoke passed.");
}
finally
{
    release.TrySetResult();
    await app.StopAsync();
}
