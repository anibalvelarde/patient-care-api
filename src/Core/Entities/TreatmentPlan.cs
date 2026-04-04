namespace Neurocorp.Api.Core.Entities;

public class TreatmentPlan : AuditableEntityBase
{
    public int PatientId { get; set; }
    public int DiscoverySessionId { get; set; }
    public int CreatedByTherapistId { get; set; }
    public string PlanStatus { get; set; } = "Draft";
    public string? ResultsDocumentUrl { get; set; }
    public int WeeklyFrequency { get; set; } = 1;
    public int DurationWeeks { get; set; } = 4;
    public string? Notes { get; set; }

    public Patient? Patient { get; set; }
    public TherapySession? DiscoverySession { get; set; }
    public Therapist? CreatedByTherapist { get; set; }
    public ICollection<TreatmentPlanLine> Lines { get; set; } = [];
}
