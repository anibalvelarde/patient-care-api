using System.ComponentModel.DataAnnotations;

namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientProfileUpdateRequest
{
    public PatientProfileUpdateRequest()
    {
        this.FirstName = string.Empty;
        this.LastName = string.Empty;
        this.MiddleName = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Gender = string.Empty;
        this.DateOfBirth = DateTime.MinValue;
        this.ActiveStatus = true;
    }

    public string FirstName { get; set; }
    public string MiddleName { get; set; }
    public string LastName { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    // B1: empty/null means "unchanged" (the repository only applies non-empty values);
    // a supplied value must match the DB enum or MySQL 1265 surfaced as an opaque 500.
    [AllowedValues(null, "", "Male", "Female", "Other")]
    public string Gender { get; set; }
    public bool ActiveStatus { get; set; }
    public string? MedicalRecordNumber { get; set; }
    public string? Cedula { get; set; }
    // WP-23 (F7): omitted/null = unchanged. Changing the stored value requires the
    // Patients.SenadisDiscount.Edit claim (gate enforced in PatientsController.UpdatePatient).
    public bool? HasSenadisDiscount { get; set; }
    // WP-24 (F3/F4): omitted/null = unchanged. Changing the stored value requires the
    // Patients.RequiresDiscovery.Edit claim (gate enforced in PatientsController.UpdatePatient).
    public bool? RequiresDiscovery { get; set; }

    public override string ToString()
    {
        return $"FN: {this.FirstName} MN: {this.MiddleName} LN: {this.LastName} e: {this.Email} p: {this.PhoneNumber} DoB: {this.DateOfBirth.ToShortDateString()} Gender: {this.Gender} Active: {this.ActiveStatus}";
    }

}
