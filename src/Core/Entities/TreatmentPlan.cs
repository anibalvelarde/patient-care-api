namespace Neurocorp.Api.Core.Entities;

public class TreatmentPlan : AuditableEntityBase
{
    /// <summary>
    /// WP-55 (B-1): the canonical <c>PlanStatus</c> values (a free-text varchar column, not a DB
    /// enum). Before this the four strings were typed literally in ~19 places — the entity/DTO
    /// defaults, and the whole <c>TreatmentPlanService</c> state machine
    /// (Draft → Active → Completed / Cancelled) plus <c>BulkSchedulingService</c> and
    /// <c>TreatmentPlanRepository</c>. One typo in a status compare silently breaks a transition.
    /// </summary>
    public static class PlanStatuses
    {
        public const string Draft = "Draft";
        public const string Active = "Active";
        public const string Completed = "Completed";
        public const string Cancelled = "Cancelled";

        public static readonly IReadOnlyList<string> All = new[] { Draft, Active, Completed, Cancelled };
    }

    public int PatientId { get; set; }
    public int DiscoverySessionId { get; set; }
    public int CreatedByTherapistId { get; set; }
    public string PlanStatus { get; set; } = PlanStatuses.Draft;
    public string? ResultsDocumentUrl { get; set; }
    public int WeeklyFrequency { get; set; } = 1;
    public int DurationWeeks { get; set; } = 4;
    public string? Notes { get; set; }

    public Patient? Patient { get; set; }
    public TherapySession? DiscoverySession { get; set; }
    public Therapist? CreatedByTherapist { get; set; }
    public ICollection<TreatmentPlanLine> Lines { get; set; } = [];
}
