using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for the therapist-payroll tables (WP-14B). Mirrors PaymentConfigurations:
// only DB column-name renames are declared; relationships are inferred by convention from the
// navigation properties (ServicePayment.SessionServicePayments / .Therapist / .PaymentType).

public class ServicePaymentConfiguration : IEntityTypeConfiguration<ServicePayment>
{
    public void Configure(EntityTypeBuilder<ServicePayment> builder)
    {
        builder.ToTable("ServicePayment");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("ServicePaymentID");
        builder.Property(e => e.TherapistId).HasColumnName("TherapistID");
        // ReversesServicePaymentID is a bare scalar (no navigation) — mapped for completeness,
        // reserved for WP-14.5 reversals; EF does not model the self-FK relationship.
        builder.Property(e => e.ReversesServicePaymentId).HasColumnName("ReversesServicePaymentID");
    }
}

public class SessionServicePaymentConfiguration : IEntityTypeConfiguration<SessionServicePayment>
{
    public void Configure(EntityTypeBuilder<SessionServicePayment> builder)
    {
        builder.ToTable("SessionServicePayment");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("SessionServicePaymentID");
        builder.Property(e => e.TherapySessionId).HasColumnName("SessionID");
        builder.Property(e => e.ServicePaymentId).HasColumnName("ServicePaymentID");
    }
}
