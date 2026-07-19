using Neurocorp.Api.Core.BusinessObjects.Lookups;

namespace Neurocorp.Api.Core.Interfaces.Services;

/// <summary>Outcome of an append attempt (controller maps to 204 / 404 / 409).</summary>
public enum PriceAppendResult
{
    Success,
    SpecialtyNotFound,
    DuplicateRow,
}

/// <summary>
/// WP-39: specialty price-sheet operations + the shared price-resolution helper that WP-40's
/// booking flow depends on (inject this service and call <see cref="ResolvePriceAsync"/> with
/// the SESSION date).
/// </summary>
public interface ISpecialtyPriceService
{
    /// <summary>Full price history for the admin modal; null = unknown specialty (404).</summary>
    Task<SpecialtyPriceHistory?> GetHistoryAsync(int specialtyTypeId);

    /// <summary>
    /// Appends effective-dated rows (append-only; history never mutated). Duplicate
    /// (durationMinutes, effectiveFrom) pairs — already on file OR repeated within the
    /// request — yield <see cref="PriceAppendResult.DuplicateRow"/>.
    /// </summary>
    Task<PriceAppendResult> AppendAsync(int specialtyTypeId, SpecialtyPricesPutRequest request);

    /// <summary>
    /// Resolution order (contract: lookups-api.md): duration row with the latest
    /// effectiveFrom ≤ <paramref name="sessionDate"/> → else DefaultAmount → else none.
    /// Unknown specialty resolves to none.
    /// </summary>
    Task<PriceResolution> ResolvePriceAsync(int specialtyTypeId, int durationMinutes, DateOnly sessionDate);
}
