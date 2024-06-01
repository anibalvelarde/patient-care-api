namespace Neurocorp.Api.Core.Entities;

public class Caretaker : PersonBase
{
    public Caretaker()
    {
        this.Relationship = "";
        this.Patients = [];
        this.Notes = string.Empty;
    }
    public string Notes { get; set; }
    public string Relationship { get; set; }
    public ICollection<PatientCaretaker> Patients { get; set; }

    public UserRole MintNewRole()
    {
        return new UserRole() {
            UserId = this.User!.Id,
            RoleId = 4 // as defined in table UserRole for Caretakers
        };
    }    
}