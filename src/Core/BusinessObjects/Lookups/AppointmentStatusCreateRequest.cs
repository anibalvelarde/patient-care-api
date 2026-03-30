using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Lookups;

public class AppointmentStatusCreateRequest
{
    [Required]
    public string StatusName { get; set; } = string.Empty;
    public string? StatusDescription { get; set; }
}
