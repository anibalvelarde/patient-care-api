using Microsoft.Extensions.DependencyInjection;
using Neurocorp.Api.Core.Interfaces.Repositories;
using Neurocorp.Api.Infrastructure.Repositories;
using Neurocorp.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Neurocorp.Api.Infrastructure.Configurations;

public static class NeurocorpConfigurationExtensions
{
    private static readonly string DEFAULT_DB_CONN_STRING = "Server={{HOST}};Port={{DB_PORT}};Database={{DB_NAME}};Uid=root;Pwd={{MYSQL_PASSWORD}};";
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Retrieve password from ENV VAR
        var dbHost = Environment.GetEnvironmentVariable("DATABASE_HOST");
        var dbPort = Environment.GetEnvironmentVariable("DATABASE_PORT");
        var dbName = Environment.GetEnvironmentVariable("DATABASE_NAME");
        var dbPassword = Environment.GetEnvironmentVariable("DATABASE_PASSWORD");
        var dbUser = Environment.GetEnvironmentVariable("DATABASE_USER");
        Console.WriteLine($"HOST: {dbHost}  PORT: {dbPort}  DB: {dbName} USER: {dbUser} PSWD: {dbPassword?.Length ?? 0} (>0 means password was given)");

        // Build cn dynamically
        var cn = (configuration.GetConnectionString("DefaultConnection") ?? DEFAULT_DB_CONN_STRING)
            .Replace("{{HOST}}", dbHost)
            .Replace("{{DB_PORT}}", dbPort)
            .Replace("{{DB_NAME}}", dbName)
            .Replace("{{DB_USER}}", dbUser)
            .Replace("{{MYSQL_PASSWORD}}", dbPassword);

        // Register ApplicationDbContext with MySQL|
        services.AddDbContextPool<ApplicationDbContext>(options =>
            options.UseMySql(
                cn!.ToString(), 
                ServerVersion.AutoDetect(cn),
                options => options.EnableRetryOnFailure(
                    maxRetryCount: 5,
                    maxRetryDelay: System.TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null
                )));
        services.AddHealthChecks()
            .AddCheck<HealthChecks.CustomDbHealthCheck>("DbChecks");

        // Register repositories
        services.AddScoped(typeof(IRepository<>), typeof(EfRepository<>));
        services.AddScoped<IPatientRepository, PatientRepository>();
        services.AddScoped<IPatientProfileRepository, PatientProfileRepository>();
        services.AddScoped<ICaretakerProfileRepository, CaretakerProfileRepository>();
        services.AddScoped<ITherapistProfileRepository, TherapistProfileRepository>();
        services.AddScoped<ICaretakerRepository, CaretakerRepository>();
        services.AddScoped<ITherapistRepository, TherapistRepository>();
        services.AddScoped<ITherapySessionRepository, TherapySessionRepository>();
        services.AddScoped<IPaymentRepository, PaymentRepository>();
        services.AddScoped<ISessionPaymentRepository, SessionPaymentRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserRoleRepository, UserRoleRepository>();
        services.AddScoped<ISessionEventRepository, SessionEventRepository>();
        return services;
    }
}
