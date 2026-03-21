namespace Neurocorp.Api.Core.Entities;

public class Caretaker : PersonBase
{
    public Caretaker()
    {
        this.Patients = [];
        this.Notes = string.Empty;
    }
    public string? Notes { get; set; }
    public ICollection<PatientCaretaker> Patients { get; set; }

    public UserRole MintNewRole()
    {
        return new UserRole() {
            UserId = this.User!.Id,
            RoleId = 4 // as defined in table UserRole for Caretakers
        };
    }
}

public class UndefinedCaretaker : Caretaker
{
    public UndefinedCaretaker()
    {
        this.User = new User { Id = int.MinValue, FirstName = string.Empty, LastName = string.Empty };
        this.Id = int.MinValue;
        this.Notes = "Undefined";
    }
}