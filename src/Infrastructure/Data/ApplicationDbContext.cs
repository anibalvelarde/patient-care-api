using Microsoft.EntityFrameworkCore;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<Patient> Patients { get; set; }
    public DbSet<Caretaker> Caretakers { get; set; }
    public DbSet<Therapist> Therapists { get; set; }
    public DbSet<TherapySession> TherapySessions { get; set; }
    
    // Override OnModelCreating if needed
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
