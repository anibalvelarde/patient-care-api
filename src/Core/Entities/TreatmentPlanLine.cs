namespace Neurocorp.Api.Core.Entities;

public class TreatmentPlanLine : AuditableEntityBase
{
    public int TreatmentPlanId { get; set; }
    public int SpecialtyTypeId { get; set; }
    public int? PreferredTherapistId { get; set; }
    public byte? DayOfWeek { get; set; }
    public TimeOnly? PreferredTime { get; set; }
    public int Duration { get; set; } = 60;
    public byte SortOrder { get; set; }

    public TreatmentPlan? TreatmentPlan { get; set; }
    public SpecialtyType? SpecialtyType { get; set; }
    public Therapist? PreferredTherapist { get; set; }
    public ICollection<TherapySession> TherapySessions { get; set; } = [];
}
