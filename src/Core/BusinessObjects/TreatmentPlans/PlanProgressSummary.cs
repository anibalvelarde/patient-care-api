namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class PlanProgressSummary
{
    public int PlanId { get; set; }
    public int TotalPlanned { get; set; }
    public int SessionsCreated { get; set; }
    public int SessionsCompleted { get; set; }
    public int SessionsCancelled { get; set; }
    public int SessionsRemaining { get; set; }
    public DateOnly? NextUpcomingDate { get; set; }
    public List<PlanSessionInfo> Sessions { get; set; } = [];
}

public class PlanSessionInfo
{
    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly SessionTime { get; set; }
    public int TreatmentPlanLineId { get; set; }
    public string SpecialtyAbbreviation { get; set; } = string.Empty;
    public string TherapistName { get; set; } = string.Empty;
    public int AppointmentStatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
}
