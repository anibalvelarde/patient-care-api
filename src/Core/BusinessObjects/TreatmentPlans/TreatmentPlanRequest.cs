namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class TreatmentPlanRequest
{
    public int PatientId { get; set; }
    public int DiscoverySessionId { get; set; }
    /// <summary>
    /// WP-61 (F-8): IGNORED. The creator is always the discovery session's therapist, set by the
    /// server on create (the UI used to send a hardcoded 1). Kept only so older clients still bind.
    /// </summary>
    public int? CreatedByTherapistId { get; set; }
    public string? ResultsDocumentUrl { get; set; }
    public int WeeklyFrequency { get; set; } = 1;
    public int DurationWeeks { get; set; } = 4;
    public string? Notes { get; set; }
    public List<TreatmentPlanLineRequest> Lines { get; set; } = [];
}
