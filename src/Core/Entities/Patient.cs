using System.Text;

namespace Neurocorp.Api.Core.Entities;

public class Patient : PersonBase
{
    public Patient()
    {
        this.Gender = string.Empty;
        this.MedicalRecordNumber = string.Empty;
    }

    public DateTime? DateOfBirth { get; set; }
    public string Gender { get; set; }
    public string MedicalRecordNumber { get; set; }
    public ICollection<PatientCaretaker>? Caretakers { get; set; }

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append("Pid: ").Append(this.Id).Append("  ")
        .Append("Uid: ").Append(this.User!.Id).Append("  ")
        .Append("DoB: ").Append((this.DateOfBirth ?? DateTime.MinValue).ToShortDateString()).Append("  ")
        .Append("G: ").Append(this.Gender).Append("  ")
        .Append("Mrn: ").Append(this.MedicalRecordNumber);
        return sb.ToString();
    }
    
    public UserRole MintNewRole()
    {
        return new UserRole() {
            UserId = this.User!.Id,
            RoleId = 2 // as defined in table UserRole for Patients
        };
    }
}

public class UndefinedPatient : Patient
{
    public UndefinedPatient()
    {
        this.Gender = "Undefined";
        this.User!.Id = int.MinValue;
        this.Id = int.MinValue;
        this.DateOfBirth = DateTime.MinValue;
    }
}