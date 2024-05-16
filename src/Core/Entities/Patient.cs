namespace Neurocorp.Api.Core.Entities;

public class Patient
{
    public Patient()
    {
        this.Gender = string.Empty;
        this.MedicalRecordNumber = string.Empty;
        this.User = new User();
    }

    public int PatientId {get; set;}
    public int UserId {get; set;}
    public DateTime? DateOfBirth {get; set;}
    public string Gender {get; set;}
    public string MedicalRecordNumber {get; set;}
    public User User {get; set;}
}

public class UndefinedPatient : Patient
{
    public UndefinedPatient()
    {
        this.Gender = "Undefined";
        this.UserId = int.MinValue;
        this.PatientId = int.MinValue;
        this.DateOfBirth = DateTime.MinValue;
    }
}