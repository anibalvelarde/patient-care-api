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
}
