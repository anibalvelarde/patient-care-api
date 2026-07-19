using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class LookupUpdateRequest
{
    [MaxLength(50)]
    public string? Abbreviation { get; set; }

    [MaxLength(75)]
    public string? Name { get; set; }

    [MaxLength(200)]
    public string? Description { get; set; }

    public int? SortOrder { get; set; }

    // WP-23 (F6): accepted only for specialty-types (null = unchanged; clearing to NULL is out of
    // scope — 0 is a legal price, not a sentinel); ignored for every other table.
    [Range(0, 99999999.99)]
    public decimal? DefaultAmount { get; set; }

    // WP-39 (PR-2/G4): accepted only for specialty-types (structural Manage claim, mirroring
    // DefaultAmount); null/omitted = unchanged; ignored for every other table.
    public bool? OfferedOnSite { get; set; }
}
