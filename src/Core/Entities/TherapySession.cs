using System.Collections.Generic;
using System.Reflection.Metadata.Ecma335;

namespace Neurocorp.Api.Core.Entities;

public class TherapySession : AuditableEntityBase
{
    // WP-29: public so SQL-side past-due candidate filters reference the same limit.
    public const int DAYS_LATE_LIMIT = 35;

    public TherapySession()
    {
        this.Notes = string.Empty;
        this.TherapyTypes = string.Empty;
    }

    public int PatientId { get; set; }
    public int TherapistId { get; set; }
    public DateOnly SessionDate { get; set; }
    public TimeOnly SessionTime { get; set; }
    public int Duration { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal ProviderAmount { get; set; }
    public decimal GrossProfit  { get; set; }
    public bool IsPaidOff { get; set; }
    public string Notes { get; set; }
    public string TherapyTypes { get; set; }
    public int AppointmentStatusId { get; set; } = 4; // Default: Completed
    public int? SiteId { get; set; }
    public int? SpecialtyTypeId { get; set; }
    public int? TreatmentPlanLineId { get; set; }
    // WP-39 (G4): on-site trip charge snapshotted at booking; NULL = not an on-site visit.
    // INERT until WP-40 — mapped so the EF model matches V030, but deliberately NOT threaded
    // through any session DTO/mapper (remember: TWO SessionEvent mappers must stay in sync).
    public decimal? OnSiteChargeAmount { get; set; }

    // WP-49 (BR3/BR4, V034). LateFeeAmount is the fee CURRENTLY IN FORCE, never the historical
    // one: NULL = never applied, 0.00 = applied then waived (original preserved in the
    // [FEE-WAIVED …] marker). That choice keeps AmountDue() unconditional — no "CASE WHEN
    // waived" to hand-mirror into the SQL predicate.
    public decimal? LateFeeAmount { get; set; }

    // The anti-compounding LATCH, deliberately separate from LateFeeAmount and deliberately
    // NOT cleared by a waive: a forgiven session must never be re-charged by the next batch.
    public DateOnly? LateFeeAppliedOn { get; set; }

    public DateOnly? FeeWaivedOn { get; set; }

    // Durable audit stamp. LastUpdatedByUserId is overwritten by the next edit of any kind and
    // so cannot answer "who forgave this fee".
    public int? FeeWaivedByUserId { get; set; }

    public Therapist? Therapist{ get; set; }
    public Patient? Patient{ get; set; }
    public AppointmentStatus? AppointmentStatus { get; set; }
    public Site? Site { get; set; }
    public SpecialtyType? SpecialtyType { get; set; }
    public TreatmentPlanLine? TreatmentPlanLine { get; set; }
    public ICollection<SessionPayment> SessionPayments { get; set; } = [];
    public ICollection<AppointmentConfirmation> Confirmations { get; set; } = [];

    /// <summary>
    /// WP-49 (ruling 2): the base the BR3 late chargeback is 30% OF — the unpaid balance
    /// excluding any late fee already applied. Split out from <see cref="AmountDue"/> so the
    /// fee can never be computed off itself and compound.
    /// </summary>
    public decimal UnpaidBalanceBeforeLateFee()
    {
        // WP-40: the on-site trip charge is part of the billed total (statements, payment
        // allocation, past-due) — but never part of the provider-fee base.
        return Amount - DiscountAmount - AmountPaid + (OnSiteChargeAmount ?? 0m);
    }

    /// <summary>
    /// The full collectible balance. ⚠️ This formula is hand-mirrored in four other places —
    /// the SQL past-due predicate in <c>SessionEventRepository</c>, the <c>IsPaidOff</c> writer
    /// and the allocation cap in <c>PaymentRecordService</c>, and the delinquency sums in
    /// <c>PatientsController</c>/<c>TherapistsController</c>. All of them must move in
    /// lockstep; the SQL one carries a comment claiming parity with this method, and that
    /// claim has to stay true.
    /// </summary>
    public decimal AmountDue()
    {
        return UnpaidBalanceBeforeLateFee() + (LateFeeAmount ?? 0m);
    }

    /// <summary>
    /// WP-49 (ruling 4): does this session carry a fee that a discount edit could erase?
    /// Drives the AM loophole guard — on a fee-bearing session, changing the discount needs
    /// <c>Sessions.Fee.Manage</c> on top of <c>Sessions.Discount.Edit</c>, otherwise an AM
    /// could zero a fee by setting discount = amount and BR4 would be decoration.
    ///
    /// Uses the LATCH, not the amount, so a waived fee still counts as fee-bearing — the
    /// waiver is the record of a manager's decision and staff should not be able to quietly
    /// rewrite the money around it.
    /// </summary>
    public bool CarriesFee()
    {
        return LateFeeAppliedOn.HasValue
               || Notes?.Contains("[NOSHOW-FEE", StringComparison.Ordinal) == true;
    }

    public bool GetPastDue()
    {
        return (AmountDue() > 0) && IsPastDeadline();
    }

    // WP-49 ruling 1: the 35-day delinquency window is UNCHANGED and runs BESIDE the 6-day
    // late-fee clock. A session is surcharged on day 7 but only becomes *delinquent* on day 36
    // — "a late fee applies" and "this is a collections problem" are different statements.
    // Do not unify these two clocks.
    private bool IsPastDeadline()
    {
        var difference = DateTime.UtcNow - SessionDate.ToDateTime(SessionTime);
        return difference.Days > DAYS_LATE_LIMIT;
    }
}

public class UndefinedSession : TherapySession
{
    public UndefinedSession()
    {
        this.Id = -9999;
        this.PatientId = -9999;
        this.TherapistId = -9999;
        this.SessionDate = DateOnly.MinValue;
        this.SessionTime = TimeOnly.MinValue;
        this.Notes = "Undefined Session";
    }
}