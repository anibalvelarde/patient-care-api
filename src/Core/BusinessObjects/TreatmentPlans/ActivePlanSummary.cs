namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class ActivePlanSummary
{
    public int PlanId { get; set; }
    public string DisplayTitle { get; set; } = string.Empty;
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string PlanStatus { get; set; } = "Active";
    public int WeeklyFrequency { get; set; }
    public int DurationWeeks { get; set; }
    public int TotalPlanned { get; set; }
    public int SessionsCreated { get; set; }
    public int SessionsCompleted { get; set; }
    public int SessionsRemaining { get; set; }
    public DateOnly? NextUpcomingDate { get; set; }
    public bool NeedsAttention { get; set; }
}
