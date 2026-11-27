using System.Net;
using System.Threading.RateLimiting;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Models.Autorization.Security;

/// <summary>Per-process admission limits. Tune against measured normal traffic; not a network-level DDoS filter.</summary>
public sealed class TrafficProtectionOptions
{
    public int GlobalPerSecond { get; set; } = 500;
    public int ClientPerSecond { get; set; } = 40;
    public int SensitivePerMinute { get; set; } = 30;
    public int ExpensivePerSecond { get; set; } = 60;
    public int ExpensiveClientPerSecond { get; set; } = 8;
    public int ConcurrentSensitiveRequests { get; set; } = 12;
    public int ConcurrentRequests { get; set; } = 128;
    public int ConcurrentExpensiveRequests { get; set; } = 24;
    public int ConcurrentHubRequests { get; set; } = 1024;
    /// <summary>Exact immediate proxy addresses that overwrite X-Forwarded-For with ONE verified client IP.</summary>
    public string[] TrustedProxies { get; set; } = [];
}

/// <summary>Shared HTTP admission policy for Auth and all node APIs. No caller-supplied account or arbitrary path keys.</summary>
public static class TrafficProtection
{
    private static readonly Meter Meter = new("MysticEgo.TrafficProtection");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("mysticego.http.admission.rejected");

    /// <summary>Bounded route categories; never use an attacker-controlled path as a partition key.</summary>
    public static string Category(PathString path)
    {
        if(path.StartsWithSegments("/hub")) return "hub";
        if(path.Equals("/api/auth/login",StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/browser/login",StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/browser/refresh",StringComparison.OrdinalIgnoreCase) ||
           path.Equals("/api/auth/audit/register",StringComparison.OrdinalIgnoreCase) ||
           path.StartsWithSegments("/api/users") && path.Value!.EndsWith("/password",StringComparison.OrdinalIgnoreCase))
            return "sensitive";
        if(path.StartsWithSegments("/mcp") || path.StartsWithSegments("/api/v1/orchestra/capacity/mcp") || path.StartsWithSegments("/api/v1/assistant") ||
           path.StartsWithSegments("/api/v1/managed/panel-assistant") ||
           path.StartsWithSegments("/api/v1/managed/ui-automation") ||
           path.StartsWithSegments("/api/v1/world-ai") || path.StartsWithSegments("/api/v1/avatars") ||
           path.StartsWithSegments("/api/v1/collective/voice")) return "expensive";
        return "standard";
    }

    private static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    /// <summary>Only accept a single forwarded address from an explicitly trusted immediate proxy.
    /// This is a rate-limit key only; never modifies the connection or authorization identity.</summary>
    public static string ClientKey(HttpContext context,IReadOnlySet<IPAddress> trusted)
    {
        var peer=context.Connection.RemoteIpAddress;
        if(peer is null)return "unknown";
        peer=Normalize(peer);
        var forwarded=context.Request.Headers["X-Forwarded-For"];
        if(trusted.Contains(peer) && forwarded.Count==1 && IPAddress.TryParse(forwarded[0],out var ip))
            peer=Normalize(ip);
        // Group IPv6 privacy addresses by /64, avoiding easy rotation within a subnet.
        if(peer.AddressFamily==System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            var bytes=peer.GetAddressBytes();
            Array.Clear(bytes,8,8);
            return new IPAddress(bytes).ToString()+"/64";
        }
        return peer.ToString();
    }

    /// <summary>Register shared global, client, sensitive, expensive and concurrency admission limits.
    /// Queues are disabled so overload is rejected instead of consuming more resources.</summary>
    public static IServiceCollection AddTrafficProtection(this IServiceCollection services,IConfiguration config)
    {
        var o=new TrafficProtectionOptions();
        config.GetSection("TrafficProtection").Bind(o);
        if(new[]{o.GlobalPerSecond,o.ClientPerSecond,o.SensitivePerMinute,o.ExpensivePerSecond,o.ExpensiveClientPerSecond,
            o.ConcurrentSensitiveRequests,o.ConcurrentRequests,o.ConcurrentExpensiveRequests,o.ConcurrentHubRequests}.Any(x=>x<1||x>100_000))
            throw new InvalidOperationException("Invalid TrafficProtection limits.");
        var trusted=o.TrustedProxies.Select(x=>IPAddress.TryParse(x,out var ip)?Normalize(ip):
            throw new InvalidOperationException("Invalid trusted proxy address.")).ToHashSet();

        services.AddRateLimiter(options=>
        {
            options.RejectionStatusCode=StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter=PartitionedRateLimiter.CreateChained(
                PartitionedRateLimiter.Create<HttpContext,string>(ctx=>RateLimitPartition.GetTokenBucketLimiter("global",_=>Bucket(o.GlobalPerSecond))),
                PartitionedRateLimiter.Create<HttpContext,string>(ctx=>RateLimitPartition.GetTokenBucketLimiter(ClientKey(ctx,trusted),_=>Bucket(o.ClientPerSecond))),
                PartitionedRateLimiter.Create<HttpContext,string>(ctx=>Category(ctx.Request.Path) switch
                {
                    "sensitive"=>RateLimitPartition.GetFixedWindowLimiter("sensitive:"+ClientKey(ctx,trusted),_=>new FixedWindowRateLimiterOptions
                    {PermitLimit=o.SensitivePerMinute,Window=TimeSpan.FromMinutes(1),QueueLimit=0,AutoReplenishment=true}),
                    "expensive"=>RateLimitPartition.GetTokenBucketLimiter("expensive:"+ClientKey(ctx,trusted),_=>Bucket(o.ExpensiveClientPerSecond)),
                    _=>RateLimitPartition.GetNoLimiter("ordinary")
                }),
                PartitionedRateLimiter.Create<HttpContext,string>(ctx=>Category(ctx.Request.Path)=="expensive"
                    ?RateLimitPartition.GetTokenBucketLimiter("expensive-global",_=>Bucket(o.ExpensivePerSecond))
                    :RateLimitPartition.GetNoLimiter("ordinary")),
                PartitionedRateLimiter.Create<HttpContext,string>(ctx=>
                {
                    var category=Category(ctx.Request.Path);
                    return RateLimitPartition.GetConcurrencyLimiter(category is "hub" or "expensive" or "sensitive"?category:"http",_=>new ConcurrencyLimiterOptions
                    {PermitLimit=category=="hub"?o.ConcurrentHubRequests:category=="expensive"?o.ConcurrentExpensiveRequests:category=="sensitive"?o.ConcurrentSensitiveRequests:o.ConcurrentRequests,QueueLimit=0});
                }));
            options.OnRejected=async (context,ct)=>
            {
                Rejected.Add(1,new KeyValuePair<string,object?>("category",Category(context.HttpContext.Request.Path)));
                var response=context.HttpContext.Response;
                response.Headers.CacheControl="no-store";
                var seconds=context.Lease.TryGetMetadata(MetadataName.RetryAfter,out var retry)?Math.Max(1,Math.Ceiling(retry.TotalSeconds)):1;
                response.Headers.RetryAfter=seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
                await response.WriteAsJsonAsync(new {code="rate_limited",retryAfterSeconds=seconds},ct);
            };
        });
        return services;
    }

    private static TokenBucketRateLimiterOptions Bucket(int rate)=>new()
    {
        TokenLimit=rate,TokensPerPeriod=rate,ReplenishmentPeriod=TimeSpan.FromSeconds(1),
        QueueLimit=0,AutoReplenishment=true
    };
}
