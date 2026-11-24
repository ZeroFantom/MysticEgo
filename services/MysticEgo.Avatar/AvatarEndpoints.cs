using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Http;

namespace MysticEgo.Avatar;

public enum AvatarNode { Audit, Global, Local, Agent, Orchestra }
public sealed record SetAvatar(string AssetId);
public sealed record ThemeReplicaManifest(ThemeManifest Theme, AvatarAsset[] Assets);

public interface IAvatarAccessPolicy
{
    string? HumanId(ClaimsPrincipal principal);
    bool IsAdmin(ClaimsPrincipal principal);
    bool ValidReplicaKey(IHeaderDictionary headers);
}

public sealed class AvatarAccessPolicy(IConfiguration configuration) : IAvatarAccessPolicy
{
    public string? HumanId(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || principal.FindFirst("token_use")?.Value == "device") return null;
        return principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
    }
    public bool IsAdmin(ClaimsPrincipal principal) => HumanId(principal) is not null &&
        principal.Claims.Any(c => (c.Type == ClaimTypes.Role || c.Type is "role" or "roles" or "permission") &&
            c.Value is "system.developer" or "system.bypass" or "global.manager" or "organization.admin" or "admin");
    public bool ValidReplicaKey(IHeaderDictionary headers)
    {
        var key = configuration["Avatar:SyncKey"];
        if (string.IsNullOrWhiteSpace(key) || key.Length < 32 || !headers.TryGetValue("X-Mystic-Sync-Key", out var actual)) return false;
        var x = System.Text.Encoding.UTF8.GetBytes(key);
        var y = System.Text.Encoding.UTF8.GetBytes(actual.ToString());
        return x.Length == y.Length && CryptographicOperations.FixedTimeEquals(x,y);
    }
}

public static class AvatarRegistration
{
    public static IServiceCollection AddAvatar(this IServiceCollection services, AvatarNode node)
    {
        services.AddSingleton<AvatarCatalog>();
        if (node is AvatarNode.Audit or AvatarNode.Agent or AvatarNode.Orchestra)
            services.AddControllers().AddApplicationPart(typeof(AvatarPersonalController).Assembly);
        services.AddSingleton<IAvatarAccessPolicy, AvatarAccessPolicy>();
        services.AddHttpClient("avatar-replica", c=>c.Timeout=TimeSpan.FromSeconds(45))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false });
        if (node is AvatarNode.Local or AvatarNode.Agent) services.AddHostedService<AvatarReplicaWorker>();
        return services;
    }
    public static string ContentType(string extension) => extension switch
    {
        ".obj" => "text/plain; charset=utf-8",
        ".vrm" or ".glb" or ".vrma" => "model/gltf-binary",
        _ => "application/octet-stream"
    };
}
