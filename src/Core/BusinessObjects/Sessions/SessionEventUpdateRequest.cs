using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEventUpdateRequest
{
    public SessionEventUpdateRequest()
    {
        this.Notes = string.Empty;
    }

    public TimeOnly SessionTime { get; set; }
    public string TherapyType { get; set; } = string.Empty;
    // WP-40: nullable — omitted means "keep the stored duration" (the UI edit surfaces don't
    // carry the session's duration and used to hardcode 60, silently overwriting it). A
    // provided value that CHANGES the stored duration is validated against the bookable set.
    public int? Duration { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal Discount { get; set; }
    public decimal ProviderAmount { get; set; }
    public bool IsPaidOff { get; set; } = false;
    public string Notes { get; set; }
    public int? AppointmentStatusId { get; set; }
    public int? TherapistId { get; set; }
    public int? SiteId { get; set; }
    public int? SpecialtyTypeId { get; set; }
}