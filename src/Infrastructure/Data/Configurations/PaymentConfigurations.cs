using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for payments. Extracted verbatim from ApplicationDbContext.OnModelCreating (Chunk 2).

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payment");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("PaymentID");
        builder.Property(e => e.CaretakerId).HasColumnName("PaidBy");
    }
}

public class SessionPaymentConfiguration : IEntityTypeConfiguration<SessionPayment>
{
    public void Configure(EntityTypeBuilder<SessionPayment> builder)
    {
        builder.ToTable("SessionPayment");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("SessionPaymentID");
        builder.Property(e => e.TherapySessionId).HasColumnName("SessionID");
        builder.Property(e => e.PaymentId).HasColumnName("PaymentID");
    }
}

public class PaymentTypeConfiguration : IEntityTypeConfiguration<PaymentType>
{
    public void Configure(EntityTypeBuilder<PaymentType> builder)
    {
        builder.ToTable("PaymentType");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("PaymentTypeID");
        builder.Property(e => e.Abbreviation).HasColumnName("PmtTypeAbbreviation");
        builder.Property(e => e.Name).HasColumnName("PmtTypeName");
        builder.Property(e => e.Description).HasColumnName("PmtTypeDescription");
    }
}
