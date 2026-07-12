namespace Neurocorp.Api.Core.BusinessObjects.Patients;

/// <summary>Outcome of an executed duplicate-patient merge (WP-22) — actual counts.</summary>
public class PatientMergeResult
{
    public int SurvivorPatientId { get; set; }
    public int EliminatedPatientId { get; set; }
    public int MergeLogId { get; set; }
    public PatientMergeExecutedCounts Counts { get; set; } = new();
    public IReadOnlyList<string> FieldsFilled { get; set; } = [];
    public DateTime MergedAtUtc { get; set; }
}

public class PatientMergeExecutedCounts
{
    public int SessionsRemapped { get; set; }
    public int PlansRemapped { get; set; }
    public int CaretakerLinksRemapped { get; set; }
    public int CaretakerLinksDeduped { get; set; }
    public int SyntheticCaretakersDeleted { get; set; }
}
