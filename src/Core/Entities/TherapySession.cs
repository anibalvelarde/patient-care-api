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
    /// The full collectible balance.
    ///
    /// <para>⚠️ <b>This formula is hand-mirrored elsewhere and the copies must move in
    /// lockstep.</b> Deliberately no count here: WP-49's plan said "five places", the doc
    /// comment that replaced it said four, and BOTH were wrong — the batched roll-up behind
    /// <c>GET /api/patients/pastdue</c> was missed, so the delinquency list and the per-patient
    /// detail disagreed about the same sessions. A number in a comment is a claim that rots;
    /// re-derive it instead:</para>
    ///
    /// <code>
    /// grep -rn "Amount - .*Discount" src/ --include=*.cs
    /// </code>
    ///
    /// <para>Every hit is either a mirror of THIS method (must include the late fee and the
    /// on-site charge) or a deliberately narrower figure. The narrow ones today: the provider-fee
    /// base in <c>SessionEventHandler</c> (fees must never enter it), <c>StatementCharge.NetCharge</c>
    /// (service charge only — the add-ons are itemized separately), and the no-show fee amount in
    /// <c>SessionFeeService</c>/<c>SessionTransitionMoneyService</c>. If you add a hit, say in a
    /// comment which kind it is.</para>
    ///
    /// <para>Roll-ups over <c>SessionEvent</c> DTOs should call <c>SessionEvent.TotalCharges()</c>
    /// rather than re-deriving; the SQL predicate in <c>SessionEventRepository</c> carries a
    /// comment claiming parity with this method, and that claim has to stay true.</para>
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
        return LateFeeAppliedOn.HasValue || HasNoShowFeeMarker();
    }

    /// <summary>The WP-42 marker stamped when a no-show fee is applied.</summary>
    public const string NoShowFeeMarkerPrefix = "[NOSHOW-FEE";

    /// <summary>
    /// Has a no-show fee ever been applied to this session? THE single definition — previously
    /// this literal was matched in four places across three files.
    ///
    /// <para><b>Why this is not <c>AppointmentStatusId == 5</c>.</b> It reads like the better
    /// signal, and it would be if status and fee stayed in step. They do not: WP-42 deliberately
    /// restores nothing when a session transitions OUT of NoShow, so after a 5→2 correction the
    /// fee money is still sitting in <c>Amount</c> while the status says Confirmed. A
    /// status-based test would report "no fee" on a session that demonstrably still carries one,
    /// and the cancellation guard would then let anyone void it — silently discarding money that
    /// only a manager is allowed to forgive.</para>
    ///
    /// <para>The converse fails too. A status-5 session with no marker (a legacy import, or any
    /// row that never went through the WP-42 choke point) would be judged fee-bearing by a
    /// status test, so cancelling it would be blocked — while <c>waive-fee</c> refuses to act
    /// without a marker. That session could then be neither cancelled nor waived.</para>
    ///
    /// <para>So the marker is the accurate signal today. It is nonetheless a text match driving
    /// an authorization-adjacent decision, which is a genuine weakness — the durable fix is the
    /// fee-event table in WP-52 H3. When that lands, this method is the ONE place to change.</para>
    /// </summary>
    public bool HasNoShowFeeMarker()
        => Notes?.Contains(NoShowFeeMarkerPrefix, StringComparison.Ordinal) == true;

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