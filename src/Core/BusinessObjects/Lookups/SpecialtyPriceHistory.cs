namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

/// <summary>
/// WP-39: full price history for one specialty (admin "Prices…" modal),
/// GET /api/lookups/specialty-types/{id}/prices. Rows are ordered by duration,
/// then newest-first per duration; <c>IsCurrent</c> marks the row in effect today.
/// </summary>
public class SpecialtyPriceHistory
{
    public int SpecialtyTypeId { get; set; }
    public decimal? DefaultAmount { get; set; }
    public bool OfferedOnSite { get; set; }
    public IReadOnlyList<SpecialtyPriceHistoryRow> Prices { get; set; } = [];
}

public class SpecialtyPriceHistoryRow
{
    public int DurationMinutes { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool IsCurrent { get; set; }
}
