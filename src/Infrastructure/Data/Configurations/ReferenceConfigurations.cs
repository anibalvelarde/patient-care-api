using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for sites and specialty reference data + the therapist-specialty link.
// Extracted verbatim from ApplicationDbContext.OnModelCreating (Chunk 2).

public class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("Site");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("SiteID");
        builder.Property(e => e.SiteName).IsRequired();
        builder.Property(e => e.RUC).IsRequired(false);
        builder.Property(e => e.Address).IsRequired(false);
        builder.Property(e => e.Latitude).IsRequired(false);
        builder.Property(e => e.Longitude).IsRequired(false);
        // WP-39 (G4): flat on-site trip charge (V030); ≥ 0 API-enforced, default 0.
        builder.Property(e => e.OnSiteTripChargeAmount).HasPrecision(10, 2);
        // WP-42 (G1): no-show fee pct (V032); 0–100 API-enforced, default 30.00.
        builder.Property(e => e.NoShowFeePct).HasPrecision(5, 2);
    }
}

public class SpecialtyTypeConfiguration : IEntityTypeConfiguration<SpecialtyType>
{
    public void Configure(EntityTypeBuilder<SpecialtyType> builder)
    {
        builder.ToTable("SpecialtyType");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("SpecialtyID");
        builder.Property(e => e.Abbreviation).HasColumnName("SpecialtyAbbreviation");
        builder.Property(e => e.Name).HasColumnName("SpecialtyName");
        builder.Property(e => e.Description).HasColumnName("SpecialtyDescription");
        builder.Property(e => e.DefaultAmount).HasPrecision(10, 2);
    }
}

// WP-39 (PR-1): temporal per-duration price sheet (V030). FK column SpecialtyTypeID references
// SpecialtyType's PK (column SpecialtyID — same pattern as TherapistSpecialty). Unique key over
// (SpecialtyTypeID, DurationMinutes, EffectiveFrom) backs the append-only 409 semantics.
public class SpecialtyDurationPriceConfiguration : IEntityTypeConfiguration<SpecialtyDurationPrice>
{
    public void Configure(EntityTypeBuilder<SpecialtyDurationPrice> builder)
    {
        builder.ToTable("SpecialtyDurationPrice");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("SpecialtyDurationPriceID");
        builder.Property(e => e.SpecialtyTypeId).HasColumnName("SpecialtyTypeID");
        builder.Property(e => e.DurationMinutes).HasColumnType("smallint");
        builder.Property(e => e.Amount).HasPrecision(10, 2);
        builder.Property(e => e.EffectiveFrom).HasColumnType("date");
        builder.HasOne(e => e.SpecialtyType)
            .WithMany(s => s.DurationPrices)
            .HasForeignKey(e => e.SpecialtyTypeId);
        builder.HasIndex(e => new { e.SpecialtyTypeId, e.DurationMinutes, e.EffectiveFrom })
            .IsUnique();
    }
}

public class TherapistSpecialtyConfiguration : IEntityTypeConfiguration<TherapistSpecialty>
{
    public void Configure(EntityTypeBuilder<TherapistSpecialty> builder)
    {
        builder.ToTable("TherapistSpecialty");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("TherapistSpecialtyID");
        builder.Property(e => e.TherapistId).HasColumnName("TherapistID");
        builder.Property(e => e.SpecialtyId).HasColumnName("SpecialtyID");
        builder.HasOne(e => e.Therapist)
            .WithMany(t => t.TherapistSpecialties)
            .HasForeignKey(e => e.TherapistId);
        builder.HasOne(e => e.SpecialtyType)
            .WithMany()
            .HasForeignKey(e => e.SpecialtyId);
        builder.HasIndex(e => new { e.TherapistId, e.SpecialtyId })
            .IsUnique();
    }
}
