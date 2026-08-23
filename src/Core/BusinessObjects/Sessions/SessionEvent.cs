using System.Text.Json.Serialization;
using Neurocorp.Api.Core.BusinessObjects.Common;
using Neurocorp.Api.Core.Services;

namespace Neurocorp.Api.Core.BusinessObjects.Sessions;

public class SessionEvent : IHasAudit
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
    public int AppointmentStatusId { get; set; } = SessionStatus.Completed;
    public string StatusName { get; set; } = SessionStatus.Names.Completed;
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
    // Appointments.ProviderAmount to AM/MGR/OWN only (FrontDesk/Accountant excluded). ProviderAmountResultFilter
    // sets this to null for callers lacking that claim, and WhenWritingNull omits the property
    // entirely from the JSON so FD never receives the figure (not just a zeroed value).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? ProviderAmount { get; set; }

    // WP-40 on-site leg: the Site's trip charge snapshotted at booking; null = in-clinic.
    // Populated by BOTH SessionEvent mappers (the two-mapper contract). Included in amountDue,
    // excluded from the provider fee.
    public decimal? OnSiteChargeAmount { get; set; }

    // WP-40: where the derived Amount came from ("durationPrice" | "defaultAmount") — set on
    // the create/derivation path only; null on list/read paths (the source is not persisted).
    public string? AmountSource { get; set; }

    // WP-49 (BR3/BR4). Populated by ALL THREE SessionEvent projections — ExtractSessionEvent,
    // BookingRepository.MapToSessionEvent, and the SessionEventHandler patch mapper. The
    // discount-edit loophole guard reads CarriesFee, so a projection that forgets these fields
    // does not just hide a number: it silently disables an authorization check.
    // Late chargeback currently in force; null = never applied, 0.00 = applied then waived.
    public decimal? LateFeeAmount { get; set; }
    public DateOnly? LateFeeAppliedOn { get; set; }
    public DateOnly? FeeWaivedOn { get; set; }

    // Mirrors TherapySession.CarriesFee() — true when a late fee has been applied (even if
    // since waived) or a [NOSHOW-FEE …] marker is on the notes. Drives both the API guard and
    // the UI's read-only discount input.
    public bool CarriesFee { get; set; }

    /// <summary>
    /// WP-49: the CHARGE side of the balance — everything billed, before payments. Exists so
    /// the delinquency roll-ups in PatientsController/TherapistsController stop hand-copying
    /// <c>Amount − Discount</c>, which silently dropped the on-site trip charge (a live
    /// pre-existing bug) and would have dropped the late fee too.
    ///
    /// Invariant: <c>TotalCharges() − AmountPaid == AmountDue</c>.
    /// </summary>
    public decimal TotalCharges()
        => Amount - Discount + (OnSiteChargeAmount ?? 0m) + (LateFeeAmount ?? 0m);

    // WP-31 (U1): additive audit block for the Session Details ⓘ popover. Populated by BOTH
    // SessionEvent mappers (ExtractSessionEvent + MapToSessionEvent — the two-mapper contract).
    public AuditInfo? Audit { get; set; }
}