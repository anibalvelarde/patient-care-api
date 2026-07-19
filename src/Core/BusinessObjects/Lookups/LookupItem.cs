namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class LookupItem
{
    public int Id { get; set; }
    public string Abbreviation { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int SortOrder { get; set; }
    // WP-23 (F6): per-type field — populated only for specialty-types; null for every other table.
    public decimal? DefaultAmount { get; set; }
    // WP-39 (PR-2): per-type field — specialty-types only; false for every other table.
    public bool OfferedOnSite { get; set; }
    // WP-39 (PR-1): CURRENT-EFFECTIVE price sheet (latest effectiveFrom ≤ today per duration;
    // durations with no effective row absent). specialty-types only; empty for other tables.
    // Read-only here — written via the dedicated /prices endpoints, never the generic POST/PUT.
    public IReadOnlyList<DurationPrice> DurationPrices { get; set; } = [];
    public DateTime CreatedTimestamp { get; set; }
    public DateTime LastUpdatedTimestamp { get; set; }
}
