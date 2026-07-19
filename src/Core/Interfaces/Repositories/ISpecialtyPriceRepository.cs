using Neurocorp.Api.Core.Entities;

namespace Neurocorp.Api.Core.Interfaces.Repositories;

/// <summary>
/// WP-39: data access for the specialty price sheet. Reads load a specialty WITH its
/// DurationPrices in one round-trip (single query with join — WP-29 no-N+1 rule); the
/// lookup projection and price endpoints then shape in memory (the sheet is tiny:
/// ≤ 5 durations × short history per specialty).
/// </summary>
public interface ISpecialtyPriceRepository
{
    /// <summary>All specialty types with their full price history, single query.</summary>
    Task<IReadOnlyList<SpecialtyType>> GetAllWithPricesAsync();

    /// <summary>One specialty with its full price history, or null when unknown.</summary>
    Task<SpecialtyType?> GetWithPricesAsync(int specialtyTypeId);

    /// <summary>Appends price rows (append-only — existing rows are never touched).</summary>
    Task AddRangeAsync(IReadOnlyCollection<SpecialtyDurationPrice> rows);
}
