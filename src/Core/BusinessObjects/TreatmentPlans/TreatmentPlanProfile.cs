namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class TreatmentPlanProfile
{
    public int Id { get; set; }
    public string DisplayTitle { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public int DiscoverySessionId { get; set; }
    public DateOnly DiscoveryDate { get; set; }
    public string DiscoverySpecialty { get; set; } = string.Empty;
    public int CreatedByTherapistId { get; set; }
    public string CreatedByTherapistName { get; set; } = string.Empty;
    public string PlanStatus { get; set; } = string.Empty;
    /// <summary>
    /// WP-61: true while the plan's lines may still be edited — Draft, or Active with no sessions
    /// scheduled yet. Once sessions exist the lines are locked (sessions copy line data at
    /// scheduling, so later edits would change nothing).
    /// </summary>
    public bool CanEditLines { get; set; }
    public string? ResultsDocumentUrl { get; set; }
    public int WeeklyFrequency { get; set; }
    public int DurationWeeks { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdatedTimestamp { get; set; }
    public List<TreatmentPlanLineProfile> Lines { get; set; } = [];
}
