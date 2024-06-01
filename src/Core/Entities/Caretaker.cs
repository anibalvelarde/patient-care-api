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
}