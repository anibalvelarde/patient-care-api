namespace Neurocorp.Api.Core.BusinessObjects.Patients;

public class PatientProfile
{
    public PatientProfile()
    {
        this.PatientName = string.Empty;
        this.MedicalRecordNumber = string.Empty;
        this.Email = string.Empty;
        this.PhoneNumber = string.Empty;
        this.Gender = string.Empty;
    }

    public int PatientId { get; set; }
    public int UserId { get; set; }
    public string PatientName { get; set; }
    public string MedicalRecordNumber { get; set; }
    public DateTime DateOfBirth { get; set; }
    public string Email { get; set; }
    public string PhoneNumber { get; set; }
    public DateTime CreatedTimestamp { get; set; }
    public string Gender { get; set; }
    public bool IsActive { get; set; }
}

public class UndefinedPatientProfile : PatientProfile
{
    public UndefinedPatientProfile()
    {
        this.PatientName = "UNDEFINED";
    }
}