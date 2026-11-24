using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Models.Commands.SignalR.Clients;

public static class ClientRuntimeExtensions
{
    public static IServiceCollection AddCommandClientRuntime(
        this IServiceCollection services,
        Action<AgentHubClientOptions> configure)
    {
        var options = new AgentHubClientOptions();
        configure(options);
        return services.AddCommandClientRuntime(options);
    }

    public static IServiceCollection AddCommandClientRuntime(
        this IServiceCollection services,
        AgentHubClientOptions options)
    {
        services.AddSingleton(options);

        services.AddSingleton<IClientCommandRegistry>(sp =>
        {
            var assemblies = options.CommandAssemblies
                             ?? new[] { Assembly.GetEntryAssembly()! }
                                 .Where(a => a is not null)
                                 .ToArray();

            var log = sp.GetService<ILogger<ClientCommandRegistry>>();
            return new ClientCommandRegistry(assemblies!, log);
        });

        services.AddSingleton<ICommandInvoker, DefaultCommandInvoker>();

        return services;
    }

    public static IServiceCollection AddAgentHubClient<TClient>(
        this IServiceCollection services)
        where TClient : AgentHubClientBase
    {
        services.AddSingleton<TClient>();
        services.AddHostedService(sp => new ClientHostedService<TClient>(sp));

        return services;
    }
}