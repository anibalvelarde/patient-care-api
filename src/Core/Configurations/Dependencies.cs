using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Neurocorp.Api.Core.Interfaces.Services;
using Neurocorp.Api.Core.Services;

namespace Neurocorp.Api.Core.Configurations;

public static class NeurocorpConfigurationExtensions
{
    public static IServiceCollection AddCoreDependencies(this IServiceCollection services, IConfiguration configuration)
    {
        // Register Core services with their implementations
        services.AddScoped<IPatientService, PatientService>();
        services.AddScoped<ITherapistService, TherapistService>();
        services.AddScoped<ITherapySessionService, TherapySessionService>();
        services.AddScoped<IPatientProfileService, PatientProfileService>();
        services.AddScoped<ITherapistProfileService, TherapistProfileService>();
        return services;
    }
}
