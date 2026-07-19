using Neurocorp.Api.Core.BusinessObjects.Common;

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

/// <summary>
/// One history row. WP-39 addendum (owner ruling 2026-07-19): carries the WP-31 additive
/// <see cref="AuditInfo"/> block — rows are append-only, so <c>audit.updatedBy</c> ≡ the user
/// who ENTERED the price ("System" for id 0/unknown; raw id never serialized). The slim
/// current-effective <c>durationPrices</c> projection on the lookup item has NO audit block.
/// </summary>
public class SpecialtyPriceHistoryRow : IHasAudit
{
    public int DurationMinutes { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
    public bool IsCurrent { get; set; }
    public AuditInfo? Audit { get; set; }
}
