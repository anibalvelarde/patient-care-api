namespace Neurocorp.Api.Core.BusinessObjects.TreatmentPlans;

public class TreatmentPlanLineRequest
{
    /// <summary>
    /// WP-61: the stored line's Id on PUT — present = update that line in place (its Id, and any
    /// link to it, survives); absent = a new line. Ignored on POST.
    /// </summary>
    public int? Id { get; set; }
    public int SpecialtyTypeId { get; set; }
    public int? PreferredTherapistId { get; set; }
    public byte? DayOfWeek { get; set; }
    public TimeOnly? PreferredTime { get; set; }
    public int Duration { get; set; } = 60;
    public byte SortOrder { get; set; }
}
