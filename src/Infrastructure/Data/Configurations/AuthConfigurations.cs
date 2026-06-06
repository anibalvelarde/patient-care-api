using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for roles, role/user claims, and user-role membership.
// Extracted verbatim from ApplicationDbContext.OnModelCreating (Chunk 2).

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRole");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("UserRoleID");
        builder.Ignore(e => e.RoleCreatedOn);
    }
}

public class RoleClaimConfiguration : IEntityTypeConfiguration<RoleClaim>
{
    public void Configure(EntityTypeBuilder<RoleClaim> builder)
    {
        builder.ToTable("RoleClaim");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("RoleClaimID");
        builder.Property(e => e.RoleId).HasColumnName("RoleID");
    }
}

public class UserClaimConfiguration : IEntityTypeConfiguration<UserClaim>
{
    public void Configure(EntityTypeBuilder<UserClaim> builder)
    {
        builder.ToTable("UserClaim");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("UserClaimID");
        builder.Property(e => e.UserId).HasColumnName("UserID");
    }
}

public class RoleTypeConfiguration : IEntityTypeConfiguration<RoleType>
{
    public void Configure(EntityTypeBuilder<RoleType> builder)
    {
        builder.ToTable("RoleType");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("RoleID");
        builder.Property(e => e.Abbreviation).HasColumnName("RoleAbbreviation");
        builder.Property(e => e.Name).HasColumnName("RoleName");
        builder.Property(e => e.Description).HasColumnName("RoleDescription");
    }
}
