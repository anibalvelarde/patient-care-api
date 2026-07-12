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
        services.AddScoped<ICaretakerProfileService, CaretakerProfileService>();
        services.AddScoped<ICaretakerProfileService, CaretakerProfileService>();
        services.AddScoped<IHandleSessionEvent, SessionEventHandler>();
        services.AddScoped<IPaymentRecordService, PaymentRecordService>();
        services.AddScoped<IServicePaymentService, ServicePaymentService>();
        services.AddScoped<IAccountStatementService, AccountStatementService>();
        services.AddScoped<ITherapistStatementService, TherapistStatementService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<ISiteProfileService, SiteProfileService>();
        services.AddScoped<ILookupService, LookupService>();
        services.AddScoped<ITreatmentPlanService, TreatmentPlanService>();
        services.AddScoped<IBulkSchedulingService, BulkSchedulingService>();
        services.AddScoped<IScheduleMatrixService, ScheduleMatrixService>();
        services.AddScoped<IPatientMergeService, PatientMergeService>();

        // Authentication / authorization (Chunk 1B). Token + current-user
        // implementations live in the Web layer (composition root) because they
        // depend on JWT and HttpContext; the orchestration + hashing are here.
        services.AddScoped<IAuthService, AuthService>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        return services;
    }
}
