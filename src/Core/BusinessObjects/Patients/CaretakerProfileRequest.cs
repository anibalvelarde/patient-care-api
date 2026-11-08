using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class CaretakerProfileRequest
{
    public CaretakerProfileRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Notes = string.Empty;
    }

    public string Notes { get; set; }
    [Required]
    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    [Required]
    public string LastName { get; set; }
    // WP-57: optional at create (WP-50C intent; the UI omits a blank email). Blank/whitespace is
    // stored as NULL by the service — SystemUser.Email is UNIQUE, so "" would collide.
    public string? Email { get; set; }
    [Required]
    public string PhoneNumber { get; set; }
    
    public bool IsActive { get; set; }
}
