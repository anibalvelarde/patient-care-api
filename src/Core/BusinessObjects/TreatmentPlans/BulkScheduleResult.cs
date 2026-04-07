namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class BulkScheduleResult
{
    public int PlanId { get; set; }
    public int SessionsCreated { get; set; }
    public List<CreatedSessionInfo> Sessions { get; set; } = [];
    public List<ScheduleConflict> Conflicts { get; set; } = [];
}

public class CreatedSessionInfo
{
    public int SessionId { get; set; }
    public int TreatmentPlanLineId { get; set; }
    public int WeekNumber { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly SessionTime { get; set; }
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public string SpecialtyAbbreviation { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ProviderAmount { get; set; }
    public decimal GrossProfit { get; set; }
}

public class ScheduleConflict
{
    public int WeekNumber { get; set; }
    public int LineId { get; set; }
    public DateOnly Date { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<SuggestedAlternative> SuggestedAlternatives { get; set; } = [];
}

public class SuggestedAlternative
{
    public int TherapistId { get; set; }
    public string TherapistName { get; set; } = string.Empty;
    public TimeOnly Time { get; set; }
    public string Type { get; set; } = string.Empty;
}
