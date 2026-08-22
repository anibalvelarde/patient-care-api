using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for the WP-54 change log (DB migration V035). Column names/types mirror V035
// exactly; the relational annotations (json / enum column types) are ignored by the InMemory test
// provider, so the model still builds there.
public class EntityChangeLogConfiguration : IEntityTypeConfiguration<EntityChangeLog>
{
    public void Configure(EntityTypeBuilder<EntityChangeLog> builder)
    {
        builder.ToTable("EntityChangeLog");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("EntityChangeLogID");
        builder.Property(e => e.OccurredAtUtc).HasColumnName("OccurredAtUtc").HasColumnType("datetime(6)");
        builder.Property(e => e.UserId).HasColumnName("UserId");
        builder.Property(e => e.EntityType).HasColumnName("EntityType").HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntityId).HasColumnName("EntityId").HasMaxLength(64).IsRequired();
        builder.Property(e => e.EntityLabel).HasColumnName("EntityLabel").HasMaxLength(160).IsRequired(false);
        builder.Property(e => e.Action).HasColumnName("Action")
            .HasConversion<string>()
            .HasColumnType("enum('Insert','Update','Delete')");
        builder.Property(e => e.Changes).HasColumnName("Changes").HasColumnType("json").IsRequired();
        builder.Property(e => e.CorrelationId).HasColumnName("CorrelationId").HasMaxLength(64).IsRequired(false);

        // Parity with V035's five secondary indexes (names match so the schema and model agree).
        builder.HasIndex(e => e.OccurredAtUtc).HasDatabaseName("idx_entitychangelog_occurred");
        builder.HasIndex(e => new { e.UserId, e.OccurredAtUtc }).HasDatabaseName("idx_entitychangelog_user_occurred");
        builder.HasIndex(e => new { e.EntityType, e.OccurredAtUtc }).HasDatabaseName("idx_entitychangelog_type_occurred");
        builder.HasIndex(e => new { e.EntityType, e.EntityId }).HasDatabaseName("idx_entitychangelog_entity");
        builder.HasIndex(e => e.CorrelationId).HasDatabaseName("idx_entitychangelog_correlation");
    }
}
