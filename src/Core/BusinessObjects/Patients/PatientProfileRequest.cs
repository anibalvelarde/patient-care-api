using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientProfileRequest
{
    public PatientProfileRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.MedicalRecordNumber = string.Empty;
        this.Cedula = string.Empty;
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
    public string Cedula { get; set; }
    [Required]
    public DateTime DateOfBirth { get; set; }
    [Required]
    public string Email { get; set; }
    [Required]
    public string PhoneNumber { get; set; }
    // B1: must match the DB enum — anything else used to surface as MySQL 1265 → opaque 500.
    [Required]
    [AllowedValues("Male", "Female", "Other")]
    public string Gender { get; set; }
    // WP-23 (F7): settable at create by any patient-creating role (Questionnaire E gates edits only).
    public bool HasSenadisDiscount { get; set; }
}
