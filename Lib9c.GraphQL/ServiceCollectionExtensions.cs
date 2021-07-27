using Lib9c.GraphQL.Queries;
using Lib9c.GraphQL.Types;
using Libplanet.Action;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lib9c.GraphQL
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddLib9c<T>(this IServiceCollection services)
            where T : IAction, new()
        {
            services.TryAddSingleton<ActivationKeyType>();

            services.TryAddSingleton<RootQuery<T>>();
            services.TryAddSingleton<ActivatedAccountsQuery<T>>();
            return services;
        }
    }
}
