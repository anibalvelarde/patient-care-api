namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class TreatmentPlanLineRequest
{
    public int SpecialtyTypeId { get; set; }
    public int? PreferredTherapistId { get; set; }
    public byte? DayOfWeek { get; set; }
    public TimeOnly? PreferredTime { get; set; }
    public int Duration { get; set; } = 60;
    public byte SortOrder { get; set; }
}
