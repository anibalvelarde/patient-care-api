namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class TreatmentPlanLineProfile
{
    public int Id { get; set; }
    public int TreatmentPlanId { get; set; }
    public int SpecialtyTypeId { get; set; }
    public string SpecialtyAbbreviation { get; set; } = string.Empty;
    public string SpecialtyName { get; set; } = string.Empty;
    public int? PreferredTherapistId { get; set; }
    public string? PreferredTherapistName { get; set; }
    public byte? DayOfWeek { get; set; }
    public TimeOnly? PreferredTime { get; set; }
    public int Duration { get; set; }
    public byte SortOrder { get; set; }
}
