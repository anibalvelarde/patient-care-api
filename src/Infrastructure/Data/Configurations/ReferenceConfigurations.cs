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
