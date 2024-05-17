using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects;

public class PatientProfileRequest
{
    public PatientProfileRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.MedicalRecordNumber = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Gender = string.Empty;
    }

    [Required]
    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    [Required]
    public string LastName { get; set; }
    public string MedicalRecordNumber { get; set; }
    [Required]
    public DateTime DateOfBirth { get; set; }
    [Required]
    public string Email { get; set; }
    [Required]
    public string PhoneNumber { get; set; }
    [Required]
    public string Gender { get; set; }
}
