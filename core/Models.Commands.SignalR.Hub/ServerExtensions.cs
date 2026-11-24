using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Models.Commands.SignalR.Hub
{
    public static class ServerExtensions
    {
        /// <summary>
        /// Регистрация базовой инфраструктуры сервера.
        /// Не регистрирует конкретные хабы — их регистрируешь сам.
        /// </summary>
        public static IServiceCollection AddCommandServerCore(this IServiceCollection services)
        {
            // Реестр и capabilities — singletons, живут всё время работы
            services.TryAddSingleton<IClientRegistry, InMemoryClientRegistry>();
            services.TryAddSingleton<ICapabilityStore, InMemoryCapabilityStore>();

            // Обработка ошибок — по умолчанию логирование
            services.TryAddSingleton<IHubErrorHandler, LoggingHubErrorHandler>();
            services.TryAddSingleton<IClientIdProvider, DefaultClientIdProvider>();

            return services;
        }

        /// <summary>
        /// Зарегистрировать диспетчер для конкретного AgentHub типа.
        /// Вызывается один раз на каждый хаб.
        /// </summary>
        public static IServiceCollection AddExecutionDispatcherFor<THub>(
            this IServiceCollection services)
            where THub : Hub<IAgentClient>
        {
            services.TryAddSingleton<IExecutionDispatcher, ExecutionDispatcher<THub>>();
            return services;
        }

        /// <summary>
        /// Зарегистрировать notifier для конкретного AdminHub типа.
        /// </summary>
        public static IServiceCollection AddAdminNotifierFor<THub>(
            this IServiceCollection services)
            where THub : Hub<IAdminClient>
        {
            services.TryAddSingleton<IAdminNotifier, AdminNotifier<THub>>();
            return services;
        }
    }
}