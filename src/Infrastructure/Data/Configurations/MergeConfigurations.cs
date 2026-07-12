using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Infrastructure.Data.Configurations;

// EF Core mapping for the duplicate-patient merge audit trail (WP-22 / DB migration V025).

public class PatientMergeLogConfiguration : IEntityTypeConfiguration<PatientMergeLog>
{
    public void Configure(EntityTypeBuilder<PatientMergeLog> builder)
    {
        builder.ToTable("PatientMergeLog");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("PatientMergeLogID");
        builder.Property(e => e.SurvivorPatientId).HasColumnName("SurvivorPatientID");
        builder.Property(e => e.EliminatedPatientId).HasColumnName("EliminatedPatientID");
        builder.Property(e => e.EliminatedUserId).HasColumnName("EliminatedUserID");
        builder.Property(e => e.EliminatedName).HasMaxLength(200);
        builder.Property(e => e.EliminatedMrn).HasMaxLength(50);
        builder.Property(e => e.EliminatedCedula).HasMaxLength(50);
        builder.Property(e => e.EliminatedNotes).IsRequired(false);
        builder.Property(e => e.FieldsFilled).HasMaxLength(200).IsRequired(false);
        // Survivor FK exists in the DB (fk_patientmergelog_survivor) but no navigation is
        // mapped — the log is written/read by id only. Eliminated* columns have NO FK by
        // design: their rows are hard-deleted in the same transaction.
    }
}
