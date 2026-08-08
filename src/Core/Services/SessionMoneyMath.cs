namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-40 (BK-1/BK-2/G5): the ONE place booking money rules live — both create paths
/// (<see cref="SessionEventHandler"/> and <see cref="BulkSchedulingService"/>) and the BK-3
/// post-booking discount-edit recompute call these. Pure and static so the two money paths
/// cannot drift (the house's known trap). Callers keep their historical rounding of the fee.
/// </summary>
public static class SessionMoneyMath
{
    /// <summary>
    /// G1 (2026-07-27 addendum: 40 added — the price sheet carries real 40-minute interview
    /// services): durations bookable on NEW bookings and duration-CHANGING edits only.
    /// Stored/legacy durations are never validated on reads or unrelated edits.
    /// </summary>
    public static bool IsBookableDuration(int minutes) => SpecialtyPricing.AllowedDurations.Contains(minutes);

    public static string BookableDurationsDisplay => string.Join(", ", SpecialtyPricing.AllowedDurations);

    /// <summary>Statutory SENADIS discount rate (Panama).</summary>
    public const decimal SenadisRate = 0.20m;

    /// <summary>
    /// The statutory floor an active-SENADIS session's discount may never drop below —
    /// applies at booking (where it is also the EXACT derived value, G2) and on BK-3 edits
    /// (where staff may go above it, never below).
    /// </summary>
    public static decimal SenadisFloor(decimal amount) => Math.Round(SenadisRate * amount, 2);

    /// <summary>
    /// G2 ruling 2026-07-18 (resolves the WP-23 "no stacking" question): the booking-time
    /// discount is DERIVED, not requested — exactly 20% when SENADIS is active at the session
    /// date, exactly 0 otherwise. Ad-hoc discounts happen post-booking via the gated BK-3 edit.
    /// </summary>
    public static decimal DeriveBookingDiscount(decimal amount, bool senadisActive)
        => senadisActive ? SenadisFloor(amount) : 0m;

    /// <summary>
    /// Therapist fee on NET (WP-23 Questionnaire E): FeePctPerSession == 0 ⇒ flat FeePerSession,
    /// else net × pct. Identical semantics to the pre-WP-40 TherapistProfile.CalculateFee.
    /// </summary>
    public static decimal ProviderFee(decimal feePerSession, decimal feePctPerSession, decimal netAmount)
        => feePctPerSession == 0 ? feePerSession : netAmount * feePctPerSession;

    /// <summary>
    /// Gross profit: net − fee, plus the on-site trip charge (WP-39 G4 ruling: the trip charge
    /// is clinic revenue — never part of the provider-fee base) and the WP-49 late chargeback
    /// (BR3/D2 — likewise 100% clinic revenue; the therapist is not paid for a payment delay).
    ///
    /// ⚠️ <paramref name="lateFee"/> has no default ON PURPOSE. Every caller must state what it
    /// means to do with a late fee. The trap this guards against is specific and was verified
    /// against live code: the recompute path in <c>SessionEventHandler</c> re-derives
    /// GrossProfit whenever a discount is edited, so a forgotten argument there would keep the
    /// fee in <c>AmountDue()</c> but silently drop it from GrossProfit — correct on the day the
    /// fee is applied, wrong the moment anyone later touches the discount, with no error and no
    /// failing test. An optional parameter would let a future caller reintroduce that by
    /// omission; a required one makes the compiler ask.
    /// </summary>
    public static decimal GrossProfit(decimal netAmount, decimal providerFee, decimal? onSiteCharge, decimal? lateFee)
        => netAmount - providerFee + (onSiteCharge ?? 0m) + (lateFee ?? 0m);

    /// <summary>G4 block message — contract copy, the UI surfaces it verbatim.</summary>
    public static string MissingPriceMessage(string specialtyName, int durationMinutes)
        => $"No price configured for {specialtyName} at {durationMinutes} min — ask a manager to set it in Admin.";
}
