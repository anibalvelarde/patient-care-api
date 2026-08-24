using Neurocorp.Api.Core.BusinessObjects.Lookups;
using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Services;

/// <summary>
/// WP-39: pure temporal price-sheet rules, shared by the lookup projection, the price-history
/// endpoint, and <c>ISpecialtyPriceService.ResolvePriceAsync</c> (WP-40's dependency). Kept
/// static and side-effect free so every consumer applies the SAME "latest effectiveFrom ≤ date"
/// rule — WP-40 resolves with the SESSION date, the lookup projection with today.
/// </summary>
public static class SpecialtyPricing
{
    /// <summary>
    /// Durations the price sheet accepts, API-enforced (no DB CHECK — a future 75-min offering
    /// is a value change here, not a migration). This is the ONE source: booking validation
    /// (SessionMoneyMath.IsBookableDuration) and the WP-55 B-4 conformance test both read it, and
    /// SpecialtyPricesPutRequest's [AllowedValues] must equal it (the guard test enforces that).
    ///
    /// WP-55 G5 (owner ruling 2026-08-23) REMOVED 40, reversing the WP-40 2026-07-27 addendum that
    /// added it for the Ent-TC/Ent-TC-P interview services: there are no 40-minute sessions —
    /// durations are 30/45/60/90/120 only. The two orphan 40-min price rows were deleted in WP-55A.
    /// Do NOT "helpfully" re-add 40.
    /// </summary>
    public static readonly IReadOnlyList<int> AllowedDurations = [30, 45, 60, 90, 120];

    /// <summary>
    /// The current-effective set as of <paramref name="asOf"/>: per duration, the row with the
    /// latest EffectiveFrom ≤ asOf (future-dated rows excluded); durations with no effective
    /// row are absent. Ordered by duration.
    /// </summary>
    public static IReadOnlyList<DurationPrice> CurrentEffective(
        IEnumerable<SpecialtyDurationPrice> rows, DateOnly asOf)
    {
        return rows
            .Where(r => r.EffectiveFrom <= asOf)
            .GroupBy(r => r.DurationMinutes)
            .Select(g => g.OrderByDescending(r => r.EffectiveFrom).First())
            .OrderBy(r => r.DurationMinutes)
            .Select(r => new DurationPrice
            {
                DurationMinutes = r.DurationMinutes,
                Amount = r.Amount,
                EffectiveFrom = r.EffectiveFrom,
            })
            .ToList();
    }

    /// <summary>
    /// Shared price-resolution rule (contract: lookups-api.md, WP-39): duration row with the
    /// latest EffectiveFrom ≤ <paramref name="onDate"/> → else DefaultAmount → else none.
    /// WP-40 calls this with the SESSION's date, not today — booked sessions never reprice.
    /// </summary>
    public static PriceResolution Resolve(SpecialtyType specialty, int durationMinutes, DateOnly onDate)
    {
        var row = specialty.DurationPrices
            .Where(r => r.DurationMinutes == durationMinutes && r.EffectiveFrom <= onDate)
            .OrderByDescending(r => r.EffectiveFrom)
            .FirstOrDefault();

        if (row is not null)
            return new PriceResolution(row.Amount, AmountSource.DurationPrice);

        if (specialty.DefaultAmount.HasValue)
            return new PriceResolution(specialty.DefaultAmount, AmountSource.DefaultAmount);

        return PriceResolution.None;
    }
}
