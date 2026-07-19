namespace Neurocorp.Api.Core.Entities;

public class SpecialtyType : LookupEntityBase
{
    public bool IsDiscovery { get; set; }
    // WP-23 (F6): default session price pre-filled at booking; NULL = no default.
    // WP-39 (G2): kept as the documented FALLBACK for durations with no price row
    // (resolution: duration row effective at the session date → DefaultAmount → none).
    public decimal? DefaultAmount { get; set; }
    // WP-39 (PR-2/G4): eligibility flag — this therapy CAN be performed on-site. Informational
    // at the specialty level; the per-booking trip charge lives on Site (WP-40 applies it).
    public bool OfferedOnSite { get; set; }
    // WP-39 (PR-1): append-only temporal price sheet (see SpecialtyDurationPrice).
    public ICollection<SpecialtyDurationPrice> DurationPrices { get; set; } = [];
}
