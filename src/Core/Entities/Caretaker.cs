namespace Neurocorp.Api.Core.Entities;

public class Caretaker : PersonBase
{
    public Caretaker()
    {
        this.Relationship = "";
        this.PatientCaretakers = [];
        this.Notes = string.Empty;
    }
    public string Notes { get; set; }
    public string Relationship { get; set; }
    public ICollection<PatientCaretaker> PatientCaretakers { get; set; }
}