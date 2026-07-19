using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class LookupCreateRequest
{
    [Required]
    [MaxLength(50)]
    public string Abbreviation { get; set; } = string.Empty;

    [Required]
    [MaxLength(75)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Description { get; set; }

    public int SortOrder { get; set; } = 0;

    // WP-23 (F6): accepted only for specialty-types; ignored for every other table.
    [Range(0, 99999999.99)]
    public decimal? DefaultAmount { get; set; }

    // WP-39 (PR-2/G4): accepted only for specialty-types (rides the structural
    // Admin.Lookups.SpecialtyType.Manage claim — it describes the service, not its price);
    // ignored for every other table. Omitted/null on create = false (DB default).
    public bool? OfferedOnSite { get; set; }
}
