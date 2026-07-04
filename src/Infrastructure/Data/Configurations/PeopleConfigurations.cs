using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for the people entities. Extracted verbatim from ApplicationDbContext.OnModelCreating
// (Chunk 2 — EF mapping modularization). Behavior is unchanged; the DB column names (PatientID,
// UserID, etc.) are preserved while the C# PK property remains Id (rename deferred to a follow-up).

public class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("Patient");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("PatientID");
        builder.Property(e => e.Cedula).HasMaxLength(50);
        builder.Property(e => e.Notes).IsRequired(false);
    }
}

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("SystemUser");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("UserID");
    }
}

public class CaretakerConfiguration : IEntityTypeConfiguration<Caretaker>
{
    public void Configure(EntityTypeBuilder<Caretaker> builder)
    {
        builder.ToTable("Caretaker");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("CaretakerID");
        builder.Property(e => e.Notes).IsRequired(false);
    }
}

public class TherapistConfiguration : IEntityTypeConfiguration<Therapist>
{
    public void Configure(EntityTypeBuilder<Therapist> builder)
    {
        builder.ToTable("Therapist");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("TherapistID");
    }
}
