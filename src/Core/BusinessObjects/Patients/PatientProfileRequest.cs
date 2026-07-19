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
    // WP-25 (F5, Questionnaire D): "Cedula | Passport" is mandatory at create — missing or
    // blank/whitespace is a 400 at the model-validation layer (was optional in WP-18; blanks
    // used to insert NULL silently). Legacy-imported NULL rows stay editable via the tolerant
    // update path in PatientProfileRepository.MapToUpdatedPatient.
    [Required]
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
    // WP-37 (SEN-1/SEN-2): expiry settable at create by any patient-creating role, same as the
    // flag (G4). Omitted/null = no expiry (G1 default — open-ended SENADIS).
    public DateTime? SenadisExpirationDate { get; set; }
    // WP-24 (F3): settable at create by any patient-creating role; DEFAULT TRUE per
    // Questionnaire C — an omitted JSON field means the new patient must complete a
    // discovery session before treatment bookings. Later edits are claim-gated.
    public bool RequiresDiscovery { get; set; } = true;
}
