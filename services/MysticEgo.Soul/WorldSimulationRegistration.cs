using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MysticEgo.Soul;

public static class WorldSimulationRegistration
{
    public static IServiceCollection AddWorldSimulationHost(this IServiceCollection services,IConfiguration cfg)
    {
        // Explicit startup gate, no storage writes or service registration when disabled.
        if(!cfg.GetValue<bool>("WorldSimulation:Enabled"))return services;
        var path=cfg["WorldAuthority:DatabasePath"];
        if(string.IsNullOrWhiteSpace(path)||!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("WorldSimulation requires an absolute authority database path.");
        var topology=cfg.GetSection("WorldSimulation:Areas").Get<SimulationArea[]>()??[];
        var passages=cfg.GetSection("WorldSimulation:Passages").Get<SimulationPassage[]>()??[];
        if(topology.Length==0)throw new InvalidOperationException("WorldSimulation requires trusted topology.");
        services.AddSingleton(sp=>new WorldAuthorityStore(path,TimeProvider.System));
        services.AddSingleton(sp=>new WorldSimulationCore(sp.GetRequiredService<WorldAuthorityStore>(),topology,passages));
        services.AddSingleton(sp=>new WorldSimulationHost(sp.GetRequiredService<WorldAuthorityStore>(),
            sp.GetRequiredService<WorldSimulationCore>(),TimeProvider.System));
        services.AddHostedService(sp=>sp.GetRequiredService<WorldSimulationHost>());
        return services;
    }
}
