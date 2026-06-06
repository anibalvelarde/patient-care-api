using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for therapy sessions, appointment confirmations, and appointment status.
// Extracted verbatim from ApplicationDbContext.OnModelCreating (Chunk 2).

public class TherapySessionConfiguration : IEntityTypeConfiguration<TherapySession>
{
    public void Configure(EntityTypeBuilder<TherapySession> builder)
    {
        builder.ToTable("TherapySession");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("SessionID");
        builder.Property(e => e.TherapyTypes).IsRequired(false);
        builder.HasOne(e => e.AppointmentStatus)
            .WithMany()
            .HasForeignKey(e => e.AppointmentStatusId);
        builder.HasMany(e => e.Confirmations)
            .WithOne(c => c.TherapySession)
            .HasForeignKey(c => c.SessionId);
        builder.HasOne(e => e.Site)
            .WithMany(s => s.TherapySessions)
            .HasForeignKey(e => e.SiteId);
        builder.HasOne(e => e.SpecialtyType)
            .WithMany()
            .HasForeignKey(e => e.SpecialtyTypeId);
        builder.HasOne(e => e.TreatmentPlanLine)
            .WithMany(tpl => tpl.TherapySessions)
            .HasForeignKey(e => e.TreatmentPlanLineId)
            .HasConstraintName("TherapySession_ibfk_planline");
        builder.Property(e => e.TreatmentPlanLineId).HasColumnName("TreatmentPlanLineID");
    }
}

public class AppointmentConfirmationConfiguration : IEntityTypeConfiguration<AppointmentConfirmation>
{
    public void Configure(EntityTypeBuilder<AppointmentConfirmation> builder)
    {
        builder.ToTable("AppointmentConfirmation");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ConfirmationID");
        builder.Property(e => e.SessionId).HasColumnName("SessionID");
    }
}

public class AppointmentStatusConfiguration : IEntityTypeConfiguration<AppointmentStatus>
{
    public void Configure(EntityTypeBuilder<AppointmentStatus> builder)
    {
        builder.ToTable("AppointmentStatus");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("AppointmentStatusID");
        builder.Property(e => e.Abbreviation).HasColumnName("StatusAbbreviation");
        builder.Property(e => e.Name).HasColumnName("StatusName");
        builder.Property(e => e.Description).HasColumnName("StatusDescription");
    }
}
