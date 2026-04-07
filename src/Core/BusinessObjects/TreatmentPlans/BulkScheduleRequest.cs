namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class BulkScheduleRequest
{
    public DateOnly StartDate { get; set; }
    public int SiteId { get; set; }
    public List<LineOverride> LineOverrides { get; set; } = [];
}

public class LineOverride
{
    public int TreatmentPlanLineId { get; set; }
    public int? TherapistId { get; set; }
    public byte? DayOfWeek { get; set; }
    public TimeOnly? Time { get; set; }
    public decimal? DiscountAmount { get; set; }
}
