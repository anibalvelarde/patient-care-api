using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;
using Neurocorp.Api.Core.Interfaces.Services;

namespace Neurocorp.Api.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private static readonly int DEFAULT_SYSTEM_USER_ID = 0;

    private readonly ICurrentUserService? _currentUserService;

    // The optional ICurrentUserService keeps existing `new ApplicationDbContext(options)`
    // call sites (notably the test suite, which uses the in-memory provider) working
    // unchanged. At runtime the DI container supplies the HttpContext-backed implementation
    // so audit columns are stamped with the real authenticated user instead of 0.
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options,
        ICurrentUserService? currentUserService = null) : base(options)
    {
        _currentUserService = currentUserService;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<RoleClaim> RoleClaims { get; set; }
    public DbSet<UserClaim> UserClaims { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Caretaker> Caretakers { get; set; }
    public DbSet<Therapist> Therapists { get; set; }
    public DbSet<TherapySession> TherapySessions { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<SessionPayment> SessionPayments { get; set; }
    public DbSet<PaymentType> PaymentTypes { get; set; }
    public DbSet<AppointmentStatus> AppointmentStatuses { get; set; }
    public DbSet<AppointmentConfirmation> AppointmentConfirmations { get; set; }
    public DbSet<Site> Sites { get; set; }
    public DbSet<RoleType> RoleTypes { get; set; }
    public DbSet<SpecialtyType> SpecialtyTypes { get; set; }
    public DbSet<TherapistSpecialty> TherapistSpecialties { get; set; }
    public DbSet<TreatmentPlan> TreatmentPlans { get; set; }
    public DbSet<TreatmentPlanLine> TreatmentPlanLines { get; set; }

    public override int SaveChanges()
    {
        var entries = ChangeTracker
            .Entries()
            .Where(e => e.Entity is AuditableEntityBase &&
                        (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entityEntry in entries)
        {
            var baseEntity = (AuditableEntityBase)entityEntry.Entity;
            baseEntity.LastUpdatedTimestamp = DateTime.UtcNow;
            baseEntity.LastUpdatedByUserId = GetCurrentUserId(); // Implement this method to get the current user ID

            if (entityEntry.State == EntityState.Added)
            {
                baseEntity.CreatedTimestamp = DateTime.UtcNow;
            }
        }

        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var entries = ChangeTracker
            .Entries()
            .Where(e => e.Entity is AuditableEntityBase &&
                        (e.State == EntityState.Added || e.State == EntityState.Modified));

        foreach (var entityEntry in entries)
        {
            var baseEntity = (AuditableEntityBase)entityEntry.Entity;
            baseEntity.LastUpdatedTimestamp = DateTime.UtcNow;
            baseEntity.LastUpdatedByUserId = GetCurrentUserId(); // Implement this method to get the current user ID

            if (entityEntry.State == EntityState.Added)
            {
                baseEntity.CreatedTimestamp = DateTime.UtcNow;
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    // EF Core mappings live in IEntityTypeConfiguration<T> classes under Data/Configurations,
    // applied here by assembly scan (Chunk 2 — modularized from a ~180-line inline block).
    // DB column names (PatientID, SessionID, …) are preserved; the C# PK rename is a deferred follow-up.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }

    private int GetCurrentUserId()
    {
        // Resolved from the authenticated principal at request time. Falls back to the
        // system id (0) for unauthenticated contexts: background/hosted services, the
        // bootstrap path, and tests that construct the context without DI.
        return _currentUserService?.UserId ?? DEFAULT_SYSTEM_USER_ID;
    }
}
