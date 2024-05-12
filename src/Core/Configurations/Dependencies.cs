using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Neurocorp.Api.Core.Configurations;

public static class NeurocorpConfigurationExtensions
{
    public static IServiceCollection AddCoreDependencies(this IServiceCollection services, IConfiguration configuration)
    {
        // add dependencies here...

        return services;
    }
}
