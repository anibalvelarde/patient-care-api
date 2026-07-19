namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

/// <summary>
/// WP-39: one current-effective price sheet entry, projected read-only into the specialty-types
/// lookup item (per duration, the row with the latest effectiveFrom ≤ today; durations with no
/// effective row are absent). Written only via PUT /api/lookups/specialty-types/{id}/prices.
/// </summary>
public class DurationPrice
{
    public int DurationMinutes { get; set; }
    public decimal Amount { get; set; }
    public DateOnly EffectiveFrom { get; set; }
}
