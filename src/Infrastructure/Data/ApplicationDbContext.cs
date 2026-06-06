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

    // Override OnModelCreating if needed
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity name mappings...
        modelBuilder.Entity<Patient>(p =>{
            p.ToTable("Patient");
            p.HasKey(e => e.Id);
            p.Property(e => e.Id).HasColumnName("PatientID");
        });
        modelBuilder.Entity<User>(u => {
            u.ToTable("SystemUser");
            u.HasKey(e => e.Id);
            u.Property(e => e.Id).HasColumnName("UserID");
        });
        modelBuilder.Entity<Caretaker>(ct => {
            ct.ToTable("Caretaker");
            ct.HasKey(e => e.Id);
            ct.Property(e => e.Id).HasColumnName("CaretakerID");
            ct.Property(e => e.Notes).IsRequired(false);
        });
        modelBuilder.Entity<Therapist>(t => {
            t.ToTable("Therapist");
            t.HasKey(e => e.Id);
            t.Property(e => e.Id).HasColumnName("TherapistID");
        });
        modelBuilder.Entity<TherapySession>(ts =>{
            ts.ToTable("TherapySession");
            ts.HasKey(e => e.Id);
            ts.Property(e => e.Id).HasColumnName("SessionID");
            ts.Property(e => e.TherapyTypes).IsRequired(false);
            ts.HasOne(e => e.AppointmentStatus)
                .WithMany()
                .HasForeignKey(e => e.AppointmentStatusId);
            ts.HasMany(e => e.Confirmations)
                .WithOne(c => c.TherapySession)
                .HasForeignKey(c => c.SessionId);
            ts.HasOne(e => e.Site)
                .WithMany(s => s.TherapySessions)
                .HasForeignKey(e => e.SiteId);
            ts.HasOne(e => e.SpecialtyType)
                .WithMany()
                .HasForeignKey(e => e.SpecialtyTypeId);
            ts.HasOne(e => e.TreatmentPlanLine)
                .WithMany(tpl => tpl.TherapySessions)
                .HasForeignKey(e => e.TreatmentPlanLineId)
                .HasConstraintName("TherapySession_ibfk_planline");
            ts.Property(e => e.TreatmentPlanLineId).HasColumnName("TreatmentPlanLineID");
        });
        modelBuilder.Entity<Site>(s => {
            s.ToTable("Site");
            s.HasKey(e => e.Id);
            s.Property(e => e.Id).HasColumnName("SiteID");
            s.Property(e => e.SiteName).IsRequired();
            s.Property(e => e.RUC).IsRequired(false);
            s.Property(e => e.Address).IsRequired(false);
            s.Property(e => e.Latitude).IsRequired(false);
            s.Property(e => e.Longitude).IsRequired(false);
        });
        modelBuilder.Entity<UserRole>(ur => {
            ur.ToTable("UserRole");
            ur.HasKey(e => e.Id);
            ur.Property(e => e.Id).HasColumnName("UserRoleID");
            ur.Ignore(e => e.RoleCreatedOn);
        });
        modelBuilder.Entity<RoleClaim>(rc => {
            rc.ToTable("RoleClaim");
            rc.HasKey(e => e.Id);
            rc.Property(e => e.Id).HasColumnName("RoleClaimID");
            rc.Property(e => e.RoleId).HasColumnName("RoleID");
        });
        modelBuilder.Entity<UserClaim>(uc => {
            uc.ToTable("UserClaim");
            uc.HasKey(e => e.Id);
            uc.Property(e => e.Id).HasColumnName("UserClaimID");
            uc.Property(e => e.UserId).HasColumnName("UserID");
        });
        modelBuilder.Entity<Payment>(p => {
            p.ToTable("Payment");
            p.HasKey(e => e.Id);
            p.Property(e => e.Id).HasColumnName("PaymentID");
            p.Property(e => e.CaretakerId).HasColumnName("PaidBy");
        });
        modelBuilder.Entity<SessionPayment>(sp => {
            sp.ToTable("SessionPayment");
            sp.HasKey(e => e.Id);
            sp.Property(e => e.Id).HasColumnName("SessionPaymentID");
            sp.Property(e => e.TherapySessionId).HasColumnName("SessionID");
            sp.Property(e => e.PaymentId).HasColumnName("PaymentID");
        });
        modelBuilder.Entity<PaymentType>(pt => {
            pt.ToTable("PaymentType");
            pt.HasKey(e => e.Id);
            pt.Property(e => e.Id).HasColumnName("PaymentTypeID");
            pt.Property(e => e.Abbreviation).HasColumnName("PmtTypeAbbreviation");
            pt.Property(e => e.Name).HasColumnName("PmtTypeName");
            pt.Property(e => e.Description).HasColumnName("PmtTypeDescription");
        });
        modelBuilder.Entity<AppointmentStatus>(ast => {
            ast.ToTable("AppointmentStatus");
            ast.HasKey(e => e.Id);
            ast.Property(e => e.Id).HasColumnName("AppointmentStatusID");
            ast.Property(e => e.Abbreviation).HasColumnName("StatusAbbreviation");
            ast.Property(e => e.Name).HasColumnName("StatusName");
            ast.Property(e => e.Description).HasColumnName("StatusDescription");
        });
        modelBuilder.Entity<AppointmentConfirmation>(ac => {
            ac.ToTable("AppointmentConfirmation");
            ac.HasKey(e => e.Id);
            ac.Property(e => e.Id).HasColumnName("ConfirmationID");
            ac.Property(e => e.SessionId).HasColumnName("SessionID");
        });
        modelBuilder.Entity<RoleType>(rt => {
            rt.ToTable("RoleType");
            rt.HasKey(e => e.Id);
            rt.Property(e => e.Id).HasColumnName("RoleID");
            rt.Property(e => e.Abbreviation).HasColumnName("RoleAbbreviation");
            rt.Property(e => e.Name).HasColumnName("RoleName");
            rt.Property(e => e.Description).HasColumnName("RoleDescription");
        });
        modelBuilder.Entity<SpecialtyType>(st => {
            st.ToTable("SpecialtyType");
            st.HasKey(e => e.Id);
            st.Property(e => e.Id).HasColumnName("SpecialtyID");
            st.Property(e => e.Abbreviation).HasColumnName("SpecialtyAbbreviation");
            st.Property(e => e.Name).HasColumnName("SpecialtyName");
            st.Property(e => e.Description).HasColumnName("SpecialtyDescription");
        });
        modelBuilder.Entity<TherapistSpecialty>(ts => {
            ts.ToTable("TherapistSpecialty");
            ts.HasKey(e => e.Id);
            ts.Property(e => e.Id).HasColumnName("TherapistSpecialtyID");
            ts.Property(e => e.TherapistId).HasColumnName("TherapistID");
            ts.Property(e => e.SpecialtyId).HasColumnName("SpecialtyID");
            ts.HasOne(e => e.Therapist)
                .WithMany(t => t.TherapistSpecialties)
                .HasForeignKey(e => e.TherapistId);
            ts.HasOne(e => e.SpecialtyType)
                .WithMany()
                .HasForeignKey(e => e.SpecialtyId);
            ts.HasIndex(e => new { e.TherapistId, e.SpecialtyId })
                .IsUnique();
        });
        modelBuilder.Entity<PatientCaretaker>(entity =>
        {
            entity.ToTable("PatientCaretaker");
            entity.HasKey(pc => pc.Id);
            entity.Property(pc => pc.Id).HasColumnName("PatientCaretakerID");

            entity.HasOne(pc => pc.Patient)
                .WithMany(p => p.Caretakers)
                .HasForeignKey(pc => pc.PatientId);

            entity.HasOne(pc => pc.Caretaker)
                .WithMany(c => c.Patients)
                .HasForeignKey(pc => pc.CaretakerId);

            entity.Property(pc => pc.PrimaryCaretaker)
                .IsRequired();

            entity.Property(pc => pc.RelationshipToPatient)
                .IsRequired(false);

            // Unique constraint to prevent duplicate caretaker assignments per patient
            entity.HasIndex(pc => new { pc.PatientId, pc.CaretakerId })
                .IsUnique();
        });
        modelBuilder.Entity<TreatmentPlan>(tp => {
            tp.ToTable("TreatmentPlan");
            tp.HasKey(e => e.Id);
            tp.Property(e => e.Id).HasColumnName("TreatmentPlanID");
            tp.Property(e => e.PatientId).HasColumnName("PatientID");
            tp.Property(e => e.DiscoverySessionId).HasColumnName("DiscoverySessionID");
            tp.Property(e => e.CreatedByTherapistId).HasColumnName("CreatedByTherapistID");
            tp.Property(e => e.ResultsDocumentUrl).IsRequired(false);
            tp.Property(e => e.Notes).IsRequired(false);
            tp.HasOne(e => e.Patient)
                .WithMany()
                .HasForeignKey(e => e.PatientId);
            tp.HasOne(e => e.DiscoverySession)
                .WithMany()
                .HasForeignKey(e => e.DiscoverySessionId);
            tp.HasOne(e => e.CreatedByTherapist)
                .WithMany()
                .HasForeignKey(e => e.CreatedByTherapistId);
            tp.HasMany(e => e.Lines)
                .WithOne(l => l.TreatmentPlan)
                .HasForeignKey(l => l.TreatmentPlanId);
        });
        modelBuilder.Entity<TreatmentPlanLine>(tpl => {
            tpl.ToTable("TreatmentPlanLine");
            tpl.HasKey(e => e.Id);
            tpl.Property(e => e.Id).HasColumnName("TreatmentPlanLineID");
            tpl.Property(e => e.TreatmentPlanId).HasColumnName("TreatmentPlanID");
            tpl.Property(e => e.PreferredTherapistId).HasColumnName("PreferredTherapistID");
            tpl.HasOne(e => e.SpecialtyType)
                .WithMany()
                .HasForeignKey(e => e.SpecialtyTypeId);
            tpl.HasOne(e => e.PreferredTherapist)
                .WithMany()
                .HasForeignKey(e => e.PreferredTherapistId);
        });
    }

    private int GetCurrentUserId()
    {
        // Resolved from the authenticated principal at request time. Falls back to the
        // system id (0) for unauthenticated contexts: background/hosted services, the
        // bootstrap path, and tests that construct the context without DI.
        return _currentUserService?.UserId ?? DEFAULT_SYSTEM_USER_ID;
    }
}
