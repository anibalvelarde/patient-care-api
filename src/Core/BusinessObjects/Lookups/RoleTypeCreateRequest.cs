using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class RoleTypeCreateRequest
{
    [Required]
    public string RoleName { get; set; } = string.Empty;
}
