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
}
