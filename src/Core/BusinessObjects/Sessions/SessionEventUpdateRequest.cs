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
    // WP-58 (F-17): nullable, same as Duration. Omitted means "keep the stored amount". Before
    // WP-58 an omitted amount bound as 0, which counted as a money change and re-derived a
    // negative fee and gross profit.
    public decimal? Amount { get; set; }
    // WP-58 (F-18): AmountPaid / IsPaidOff are deliberately NOT part of this request. They are
    // derived from the payment ledger only (PaymentRecordService). A stale client used to send
    // back the AmountPaid it had loaded and silently erase payments recorded in the meantime.
    // Clients that still send them are harmless: unknown JSON fields are ignored.
    public decimal Discount { get; set; }
    public decimal ProviderAmount { get; set; }
    public string Notes { get; set; }
    public int? AppointmentStatusId { get; set; }
    public int? TherapistId { get; set; }
    public int? SiteId { get; set; }
    public int? SpecialtyTypeId { get; set; }
}