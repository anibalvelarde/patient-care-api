namespace Neurocorp.Api.Core.Entities;

/// <summary>
/// Append-only audit row for a duplicate-patient merge (WP-22). The eliminated patient's
/// Patient and SystemUser rows are hard-deleted in the same transaction that writes this row,
/// so the Eliminated* columns are identity snapshots with deliberately no FK. Maps to the
/// <c>PatientMergeLog</c> table created in migration V025.
/// </summary>
public class PatientMergeLog : AuditableEntityBase
{
    public int SurvivorPatientId { get; set; }
    public int EliminatedPatientId { get; set; }
    public int EliminatedUserId { get; set; }
    public string EliminatedName { get; set; } = string.Empty;
    public string? EliminatedMrn { get; set; }
    public string? EliminatedCedula { get; set; }
    public DateTime? EliminatedDateOfBirth { get; set; }
    public string? EliminatedNotes { get; set; }
    public int SessionsRemapped { get; set; }
    public int PlansRemapped { get; set; }
    public int CaretakerLinksRemapped { get; set; }
    public int CaretakerLinksDeduped { get; set; }
    public int SyntheticCaretakersDeleted { get; set; }
    public string? FieldsFilled { get; set; }
    public int MergedByUserId { get; set; }
}
