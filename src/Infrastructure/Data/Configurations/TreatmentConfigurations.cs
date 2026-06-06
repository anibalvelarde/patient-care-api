using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for the patient-caretaker link and treatment plans/lines.
// Extracted verbatim from ApplicationDbContext.OnModelCreating (Chunk 2).

public class PatientCaretakerConfiguration : IEntityTypeConfiguration<PatientCaretaker>
{
    public void Configure(EntityTypeBuilder<PatientCaretaker> builder)
    {
        builder.ToTable("PatientCaretaker");
        builder.HasKey(pc => pc.Id);
        builder.Property(pc => pc.Id).HasColumnName("PatientCaretakerID");

        builder.HasOne(pc => pc.Patient)
            .WithMany(p => p.Caretakers)
            .HasForeignKey(pc => pc.PatientId);

        builder.HasOne(pc => pc.Caretaker)
            .WithMany(c => c.Patients)
            .HasForeignKey(pc => pc.CaretakerId);

        builder.Property(pc => pc.PrimaryCaretaker)
            .IsRequired();

        builder.Property(pc => pc.RelationshipToPatient)
            .IsRequired(false);

        // Unique constraint to prevent duplicate caretaker assignments per patient
        builder.HasIndex(pc => new { pc.PatientId, pc.CaretakerId })
            .IsUnique();
    }
}

public class TreatmentPlanConfiguration : IEntityTypeConfiguration<TreatmentPlan>
{
    public void Configure(EntityTypeBuilder<TreatmentPlan> builder)
    {
        builder.ToTable("TreatmentPlan");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("TreatmentPlanID");
        builder.Property(e => e.PatientId).HasColumnName("PatientID");
        builder.Property(e => e.DiscoverySessionId).HasColumnName("DiscoverySessionID");
        builder.Property(e => e.CreatedByTherapistId).HasColumnName("CreatedByTherapistID");
        builder.Property(e => e.ResultsDocumentUrl).IsRequired(false);
        builder.Property(e => e.Notes).IsRequired(false);
        builder.HasOne(e => e.Patient)
            .WithMany()
            .HasForeignKey(e => e.PatientId);
        builder.HasOne(e => e.DiscoverySession)
            .WithMany()
            .HasForeignKey(e => e.DiscoverySessionId);
        builder.HasOne(e => e.CreatedByTherapist)
            .WithMany()
            .HasForeignKey(e => e.CreatedByTherapistId);
        builder.HasMany(e => e.Lines)
            .WithOne(l => l.TreatmentPlan)
            .HasForeignKey(l => l.TreatmentPlanId);
    }
}

public class TreatmentPlanLineConfiguration : IEntityTypeConfiguration<TreatmentPlanLine>
{
    public void Configure(EntityTypeBuilder<TreatmentPlanLine> builder)
    {
        builder.ToTable("TreatmentPlanLine");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("TreatmentPlanLineID");
        builder.Property(e => e.TreatmentPlanId).HasColumnName("TreatmentPlanID");
        builder.Property(e => e.PreferredTherapistId).HasColumnName("PreferredTherapistID");
        builder.HasOne(e => e.SpecialtyType)
            .WithMany()
            .HasForeignKey(e => e.SpecialtyTypeId);
        builder.HasOne(e => e.PreferredTherapist)
            .WithMany()
            .HasForeignKey(e => e.PreferredTherapistId);
    }
}
