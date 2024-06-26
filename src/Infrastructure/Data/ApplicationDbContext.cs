using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    private static readonly int DEFAULT_SYSTEM_USER_ID = 0;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Caretaker> Caretakers { get; set; }
    public DbSet<Therapist> Therapists { get; set; }
    public DbSet<TherapySession> TherapySessions { get; set; }

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
            ct.Property(e => e.Id).HasColumnName("CareTakerID");
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
        });
        modelBuilder.Entity<UserRole>(ur => {
            ur.ToTable("UserRole");
            ur.HasKey(e => e.Id);
            ur.Property(e => e.Id).HasColumnName("UserRoleID");
            ur.Ignore(e => e.RoleCreatedOn);
        });
        modelBuilder.Entity<PatientCaretaker>(entity =>
        {
            entity.ToTable("PatientCareTakers");
            entity.HasKey(pc => new { pc.PatientId, pc.CaretakerId });

            entity.HasOne(pc => pc.Patient)
                .WithMany(p => p.Caretakers)
                .HasForeignKey(pc => pc.PatientId);

            entity.HasOne(pc => pc.Caretaker)
                .WithMany(c => c.Patients)
                .HasForeignKey(pc => pc.CaretakerId);

            entity.Property(pc => pc.PrimaryCaretaker)
                .IsRequired();

            // Unique constraint to ensure only one primary care-taker per patient
            entity.HasIndex(pc => new { pc.PatientId, pc.PrimaryCaretaker })
                .IsUnique()
                .HasFilter("[PrimaryCareTaker] = 1");
        });
    }

    private int GetCurrentUserId()
    {
        // Your logic to get the current user ID
        return DEFAULT_SYSTEM_USER_ID; // Placeholder implementation
    }    
}
