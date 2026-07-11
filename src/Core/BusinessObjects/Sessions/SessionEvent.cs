using System.Text.Json.Serialization;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEvent
{
    public SessionEvent()
    {
        this.Patient = string.Empty;
        this.Therapist = string.Empty;
        this.Notes = string.Empty;
    }

    public int SessionId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly SessionTime { get; set; }
    public string Patient { get; set; }
    public string Therapist { get; set; }
    public string? TherapyTypes { get; set; } = "N/A";
    public decimal Amount { get; set; }
    public decimal Discount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountDue { get; set; }
    public bool IsPastDue { get; set; }
    public bool IsPaidOff { get; set; }
    public string Notes { get; set; }
    public int PatientId { get; set; }
    public int TherapistId { get; set;}
    public int AppointmentStatusId { get; set; } = 4;
    public string StatusName { get; set; } = "Completed";
    public bool IsConfirmed { get; set; }
    public int? SiteId { get; set; }
    public string? SiteName { get; set; }
    public int? SpecialtyTypeId { get; set; }
    public string? SpecialtyAbbreviation { get; set; }
    public string? SpecialtyName { get; set; }
    public bool? IsDiscovery { get; set; }

    // B3 (2026-07-07 punch list): primary caretaker contact info for the Session Details panel.
    // Falls back to the first linked caretaker when none is flagged primary; null when unlinked.
    public string? CaretakerName { get; set; }
    public string? CaretakerPhone { get; set; }
    public string? CaretakerEmail { get; set; }

    // WP-17 access-control: ProviderAmount (therapist payout) is confidential — matrix grants
    // Appointments.ProviderAmount to MGR/AM only (FrontDesk excluded). ProviderAmountResultFilter
    // sets this to null for callers lacking that claim, and WhenWritingNull omits the property
    // entirely from the JSON so FD never receives the figure (not just a zeroed value).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? ProviderAmount { get; set; }
}