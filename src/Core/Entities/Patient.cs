namespace Neurocorp.Api.Core.Entities;

public class Patient
{
    public Patient()
    {
        this.Name = "";
        this.TherapySessions = [];
        this.Payments = [];
    }

    public int Id {get; set;}
    public string Name {get; set;}
    public DateTime DateOfBirth {get; set;}
    public ICollection<TherapySession> TherapySessions { get; set; }
    public ICollection<Payment> Payments { get; set; }
}

public class UndefinedPatient : Patient
{
    public UndefinedPatient()
    {
        this.Name = "Undefined";
        this.DateOfBirth = DateTime.MinValue;
    }
}