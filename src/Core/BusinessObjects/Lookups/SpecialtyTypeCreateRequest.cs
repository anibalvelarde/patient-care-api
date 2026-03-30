using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class SpecialtyTypeCreateRequest
{
    [Required]
    public string Abbreviation { get; set; } = string.Empty;
    [Required]
    public string Name { get; set; } = string.Empty;
}
