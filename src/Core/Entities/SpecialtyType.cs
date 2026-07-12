namespace Neurocorp.Api.Core.Entities;

public class SpecialtyType : LookupEntityBase
{
    public bool IsDiscovery { get; set; }
    // WP-23 (F6): default session price pre-filled at booking; NULL = no default.
    public decimal? DefaultAmount { get; set; }
}
