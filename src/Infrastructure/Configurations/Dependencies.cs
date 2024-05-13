using Microsoft.Extensions.DependencyInjection;
using Neurocorp.Api.Core.Interfaces;
using Neurocorp.Api.Infrastructure.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace Neurocorp.Api.Infrastructure.Configurations;

public static class NeurocorpConfigurationExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register ApplicationDbContext with MySQL
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseMySql(configuration.GetConnectionString("DefaultConnection"),
            new MySqlServerVersion(new Version(8, 0, 25))));

            // Register repositories
            services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
            services.AddScoped<IPatientRepository, PatientRepository>();
            services.AddScoped<ICaretakerRepository, CaretakerRepository>();
            services.AddScoped<ITherapistRepository, TherapistRepository>();
            services.AddScoped<ITherapySessionRepository, TherapySessionRepository>();
            services.AddScoped<IPaymentRepository, PaymentRepository>();
            services.AddScoped<ISessionPaymentRepository, SessionPaymentRepository>();

        return services;
    }
}
