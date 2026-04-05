namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class TreatmentPlanRequest
{
    public int PatientId { get; set; }
    public int DiscoverySessionId { get; set; }
    public int CreatedByTherapistId { get; set; }
    public string? ResultsDocumentUrl { get; set; }
    public int WeeklyFrequency { get; set; } = 1;
    public int DurationWeeks { get; set; } = 4;
    public string? Notes { get; set; }
    public List<TreatmentPlanLineRequest> Lines { get; set; } = [];
}
