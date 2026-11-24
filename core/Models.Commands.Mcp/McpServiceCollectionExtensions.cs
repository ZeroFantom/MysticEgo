using Microsoft.Extensions.DependencyInjection;

namespace Models.Commands.Mcp
{
    public static class McpServiceCollectionExtensions
    {
        /// <summary>
        /// Регистрирует MCP-фасад над capability-системой.
        ///
        /// Требует:
        ///   • ICapabilityStore      — обычно из AddCommandServerCore
        ///   • IExecutionDispatcher  — обычно из AddExecutionDispatcherFor
        ///
        /// После регистрации вызови app.MapMcpEndpoints() — откроет /mcp.
        /// </summary>
        public static IServiceCollection AddCommandMcpServer(this IServiceCollection services)
        {
            services.AddSingleton<IMcpConverter, McpConverter>();
            services.AddSingleton<IMcpToolExecutor, McpToolExecutor>();
            services.AddSingleton<IMcpDispatcher, McpDispatcher>();

            return services;
        }
    }
}