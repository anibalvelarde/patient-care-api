using System.Text;

namespace Neurocorp.Api.Core.Entities;

public class Patient
{
    public Patient()
    {
        this.Gender = string.Empty;
        this.MedicalRecordNumber = string.Empty;
    }

    public int PatientId { get; set; }
    public int UserId { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; }
    public string MedicalRecordNumber { get; set; }
    public User? User { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("Pid: ").Append(this.PatientId).Append("  ")
        .Append("Uid: ").Append(this.UserId).Append("  ")
        .Append("DoB: ").Append((this.DateOfBirth ?? DateTime.MinValue).ToShortDateString()).Append("  ")
        .Append("G: ").Append(this.Gender).Append("  ")
        .Append("Mrn: ").Append(this.MedicalRecordNumber);
        return sb.ToString();
    }
    
    public UserRole MintNewRole()
    {
        return new UserRole() {
            UserId = this.UserId,
            RoleId = 2 // as defined in table UserRole for Patients
        };
    }
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