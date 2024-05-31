using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Patient> Patients { get; set; }
    public DbSet<Caretaker> Caretakers { get; set; }
    public DbSet<Therapist> Therapists { get; set; }
    public DbSet<TherapySession> TherapySessions { get; set; }

    // Override OnModelCreating if needed
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity name mappings...
        modelBuilder.Entity<Patient>().ToTable("Patient");
        modelBuilder.Entity<User>().ToTable("SystemUser");
        modelBuilder.Entity<Caretaker>().ToTable("Caretaker");
        modelBuilder.Entity<Therapist>().ToTable("Therapist");
        modelBuilder.Entity<TherapySession>().ToTable("TherapySession");
        modelBuilder.Entity<UserRole>()
            .ToTable("UserRole")
            .Ignore(ur => ur.RoleCreatedOn);

        // Configure PK for Entities
        modelBuilder.Entity<User>()
            .HasKey(s => s.UserId);
        modelBuilder.Entity<Patient>()
            .HasKey(s => s.PatientId);

    }
}
